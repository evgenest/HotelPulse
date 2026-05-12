using HotelPulse.Api.Models;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace HotelPulse.Api.Messaging;

public sealed class BookingPublisher : IAsyncDisposable
{
    private const string ExchangeName = "hotelpulse";
    private const string QueueName = "bookings.created";
    private const string RoutingKey = "booking.created";

    private readonly ConnectionFactory _factory;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;

    public BookingPublisher(ConnectionFactory factory)
    {
        _factory = factory;
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true }) return _channel;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_channel is { IsOpen: true }) return _channel;

            _connection = await _factory.CreateConnectionAsync(ct);
            _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

            await _channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: ct);
            await _channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
            await _channel.QueueBindAsync(QueueName, ExchangeName, RoutingKey, cancellationToken: ct);

            return _channel;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task PublishAsync(BookingMessage message, CancellationToken ct = default)
    {
        var channel = await EnsureChannelAsync(ct);

        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        var body = Encoding.UTF8.GetBytes(json);

        var props = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
        };

        await channel.BasicPublishAsync(ExchangeName, RoutingKey, mandatory: false, basicProperties: props, body: body, cancellationToken: ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        _initLock.Dispose();
    }
}

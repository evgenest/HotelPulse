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
    private readonly ILogger<BookingPublisher> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;

    public BookingPublisher(ConnectionFactory factory, ILogger<BookingPublisher> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true }) return _channel;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_channel is { IsOpen: true }) return _channel;

            var previousChannel = _channel;
            var previousConnection = _connection;
            var connection = await _factory.CreateConnectionAsync(ct);
            var channel = await connection.CreateChannelAsync(cancellationToken: ct);

            try
            {
                await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: ct);
                await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
                await channel.QueueBindAsync(QueueName, ExchangeName, RoutingKey, cancellationToken: ct);
            }
            catch
            {
                await channel.DisposeAsync();
                await connection.DisposeAsync();
                throw;
            }

            _connection = connection;
            _channel = channel;

            await DisposeStaleChannelAsync(previousChannel);
            await DisposeStaleConnectionAsync(previousConnection);

            return channel;
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
        await DisposeStaleChannelAsync(_channel);
        await DisposeStaleConnectionAsync(_connection);
        _initLock.Dispose();
    }

    private async ValueTask DisposeStaleChannelAsync(IChannel? channel)
    {
        if (channel is null) return;

        try
        {
            await channel.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to dispose stale RabbitMQ channel");
        }
    }

    private async ValueTask DisposeStaleConnectionAsync(IConnection? connection)
    {
        if (connection is null) return;

        try
        {
            await connection.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to dispose stale RabbitMQ connection");
        }
    }
}

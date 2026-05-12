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

    private readonly IConnection _connection;
    private readonly IChannel _channel;

    private BookingPublisher(IConnection connection, IChannel channel)
    {
        _connection = connection;
        _channel = channel;
    }

    public static async Task<BookingPublisher> CreateAsync(ConnectionFactory factory, CancellationToken ct = default)
    {
        var connection = await factory.CreateConnectionAsync(ct);
        var channel = await connection.CreateChannelAsync(cancellationToken: ct);

        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(QueueName, ExchangeName, RoutingKey, cancellationToken: ct);

        return new BookingPublisher(connection, channel);
    }

    public async Task PublishAsync(BookingMessage message, CancellationToken ct = default)
    {
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

        await _channel.BasicPublishAsync(ExchangeName, RoutingKey, mandatory: false, basicProperties: props, body: body, cancellationToken: ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.CloseAsync();
        await _connection.CloseAsync();
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

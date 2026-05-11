using HotelPulse.Api.Models;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace HotelPulse.Api.Messaging;

public sealed class BookingPublisher : IDisposable
{
    private const string ExchangeName = "hotelpulse";
    private const string QueueName = "bookings.created";
    private const string RoutingKey = "booking.created";

    private readonly IConnection _connection;
    private readonly IModel _channel;

    public BookingPublisher(ConnectionFactory factory)
    {
        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.ExchangeDeclare(ExchangeName, ExchangeType.Topic, durable: true);
        _channel.QueueDeclare(QueueName, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind(QueueName, ExchangeName, RoutingKey);
    }

    public void Publish(BookingMessage message)
    {
        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        var body = Encoding.UTF8.GetBytes(json);

        var props = _channel.CreateBasicProperties();
        props.Persistent = true;
        props.ContentType = "application/json";

        _channel.BasicPublish(ExchangeName, RoutingKey, props, body);
    }

    public void Dispose()
    {
        _channel.Close();
        _connection.Close();
    }
}

using System.Text;
using System.Text.Json;
using HotelPulse.Domain;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace HotelPulse.Infrastructure.Messaging;

public interface IBookingPublisher
{
    Task PublishAsync(BookingMessage message, CancellationToken ct = default);
}

public sealed class BookingPublisher : IBookingPublisher, IAsyncDisposable
{
    private const string ExchangeName = "hotelpulse";
    public const string QueueName = "bookings.created";
    public const string RoutingKey = "booking.created";

    private readonly ConnectionFactory _factory;
    private readonly ILogger<BookingPublisher> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;

    public BookingPublisher(HotelPulseSettings settings, ILogger<BookingPublisher> logger)
    {
        _factory = new ConnectionFactory { Uri = new Uri(settings.RabbitMqUri) };
        _logger = logger;
    }

    public async Task PublishAsync(BookingMessage message, CancellationToken ct = default)
    {
        var channel = await EnsureChannelAsync(ct);
        var json = JsonSerializer.Serialize(message, HotelPulseJson.Serializer);
        var body = Encoding.UTF8.GetBytes(json);
        var props = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
        };

        await channel.BasicPublishAsync(
            ExchangeName,
            RoutingKey,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: ct);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeQuietlyAsync(_channel);
        await DisposeQuietlyAsync(_connection);
        _initLock.Dispose();
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
            return _channel;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_channel is { IsOpen: true })
                return _channel;

            var previousChannel = _channel;
            var previousConnection = _connection;
            var connection = await _factory.CreateConnectionAsync(ct);
            var channel = await connection.CreateChannelAsync(cancellationToken: ct);

            try
            {
                await DeclareTopologyAsync(channel, ct);
            }
            catch
            {
                await channel.DisposeAsync();
                await connection.DisposeAsync();
                throw;
            }

            _connection = connection;
            _channel = channel;
            await DisposeQuietlyAsync(previousChannel);
            await DisposeQuietlyAsync(previousConnection);
            return channel;
        }
        finally
        {
            _initLock.Release();
        }
    }

    internal static async Task DeclareTopologyAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(QueueName, ExchangeName, RoutingKey, cancellationToken: ct);
    }

    private async ValueTask DisposeQuietlyAsync(IAsyncDisposable? resource)
    {
        if (resource is null)
            return;

        try
        {
            await resource.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to dispose a stale RabbitMQ resource");
        }
    }
}

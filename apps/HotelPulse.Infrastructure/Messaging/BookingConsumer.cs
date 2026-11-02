using System.Text;
using System.Text.Json;
using HotelPulse.Domain;
using HotelPulse.Infrastructure.Caching;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace HotelPulse.Infrastructure.Messaging;

public sealed class BookingConsumer(
    BookingProcessor processor,
    RedisHotelCache cache,
    HotelPulseSettings settings,
    ILogger<BookingConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConsumerLoop(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RabbitMQ error — reconnecting in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task RunConsumerLoop(CancellationToken ct)
    {
        var factory = new ConnectionFactory { Uri = new Uri(settings.RabbitMqUri) };
        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await BookingPublisher.DeclareTopologyAsync(channel, ct);
        await channel.BasicQosAsync(0, 1, false, ct);

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = ct.Register(() => done.TrySetResult());

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            var action = await HandleAsync(delivery, ct);
            if (action is null)
                return;

            if (action == QueueAction.Ack)
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
            else
                await channel.BasicNackAsync(
                    delivery.DeliveryTag,
                    multiple: false,
                    requeue: action == QueueAction.Requeue,
                    cancellationToken: CancellationToken.None);
        };

        await channel.BasicConsumeAsync(BookingPublisher.QueueName, autoAck: false, consumer, ct);
        logger.LogInformation("Consuming from {Queue}", BookingPublisher.QueueName);
        await done.Task;
    }

    private async Task<QueueAction?> HandleAsync(BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        string? bookingId = null;
        try
        {
            var json = Encoding.UTF8.GetString(delivery.Body.ToArray());
            bookingId = TryReadBookingId(json);
            var message = JsonSerializer.Deserialize<BookingMessage>(json, HotelPulseJson.Serializer);
            if (message is null)
                throw new PermanentProcessingException("Booking message was empty.");

            bookingId = message.BookingId;
            var result = await processor.ProcessAsync(message, ct);
            if (result.Changed)
                await cache.InvalidateHotelAsync(message.HotelId);

            logger.LogInformation("Booking {Id} → {Status}", message.BookingId, result.Status);
            return QueueAction.Ack;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing booking {Id}", bookingId);
            var plan = DeliveryPolicy.Decide(ExceptionClassifier.Classify(ex), delivery.Redelivered);
            if (plan.MarkRejected && bookingId is not null)
            {
                try
                {
                    await processor.MarkFailedAsync(bookingId, CancellationToken.None);
                    var hotelId = HotelIdFrom(delivery);
                    if (!string.IsNullOrEmpty(hotelId))
                        await cache.InvalidateHotelAsync(hotelId);
                }
                catch (Exception markEx)
                {
                    logger.LogError(markEx, "Could not mark booking {Id} failed; requeueing", bookingId);
                    return QueueAction.Requeue;
                }
            }

            return plan.Action;
        }
    }

    private static string? TryReadBookingId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("bookingId", out var id) ? id.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? HotelIdFrom(BasicDeliverEventArgs delivery)
    {
        try
        {
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(delivery.Body.ToArray()));
            return doc.RootElement.TryGetProperty("hotelId", out var id) ? id.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

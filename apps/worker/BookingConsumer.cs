using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace HotelPulse.Worker;

public sealed class BookingConsumer : BackgroundService
{
    private const int NoCurrentEvent = -1;
    private const int CompletedThroughMessageDeliveredEvent = 2;
    private const int CompletedThroughReservationLockedEvent = 3;

    private readonly ILogger<BookingConsumer> _logger;
    private readonly string _mongoUri;
    private readonly string _rabbitUri;

    public BookingConsumer(ILogger<BookingConsumer> logger, IConfiguration config)
    {
        _logger = logger;
        _mongoUri = config["MONGO_URI"] ?? "mongodb://localhost:27017";
        _rabbitUri = config["RABBITMQ_URI"] ?? "amqp://guest:guest@localhost:5672/";

        // MongoDB camelCase convention
        ConventionRegistry.Register(
            "camelCase",
            new ConventionPack { new CamelCaseElementNameConvention() },
            _ => true
        );
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var mongoClient = new MongoClient(_mongoUri);
        var db = mongoClient.GetDatabase("hotelpulse");
        var bookings = db.GetCollection<Booking>("bookings");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConsumerLoop(bookings, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RabbitMQ error — reconnecting in 5s");
                await Task.Delay(5_000, stoppingToken);
            }
        }
    }

    private async Task RunConsumerLoop(IMongoCollection<Booking> bookings, CancellationToken ct)
    {
        var factory = new ConnectionFactory { Uri = new Uri(_rabbitUri) };
        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

        await channel.ExchangeDeclareAsync("hotelpulse", ExchangeType.Topic, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync("bookings.created", durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync("bookings.created", "hotelpulse", "booking.created", cancellationToken: ct);
        await channel.BasicQosAsync(0, 1, false, ct); // process one message at a time

        var tcs = new TaskCompletionSource();
        using var registration = ct.Register(() => tcs.TrySetCanceled(ct));

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                var msg = JsonSerializer.Deserialize<BookingMessage>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (msg is null)
                {
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: CancellationToken.None);
                    return;
                }

                _logger.LogInformation("[worker] Processing booking {Id}", msg.BookingId);
                await ProcessBookingAsync(bookings, msg, ct);

                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: CancellationToken.None);
            }
        };

        await channel.BasicConsumeAsync("bookings.created", autoAck: false, consumer, ct);
        _logger.LogInformation("[worker] Consuming from bookings.created");

        await tcs.Task; // wait until cancelled
    }

    private async Task ProcessBookingAsync(
        IMongoCollection<Booking> col,
        BookingMessage msg,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Step 1 – mark message as delivered (700ms)
        await Task.Delay(700, ct);
        await PatchEventsAsync(col, msg.BookingId, CompletedThroughMessageDeliveredEvent, now, ct);

        // Step 2 – lock reservation in Mongo (1500ms)
        await Task.Delay(800, ct);
        await PatchEventsAsync(col, msg.BookingId, CompletedThroughReservationLockedEvent, now, ct);

        // Step 3 – finalise (900ms)
        await Task.Delay(900, ct);

        var outcome = Random.Shared.NextDouble() < 0.9 ? "confirmed" : "rejected";
        var timeStr = DateTime.UtcNow.ToString("HH:mm:ss");

        var filter = Builders<Booking>.Filter.Eq(b => b.Id, msg.BookingId);

        var update = Builders<Booking>.Update
            .Set(b => b.Status, outcome)
            .Set(b => b.ConfirmationCode,
                outcome == "confirmed"
                    ? "HP-" + Guid.NewGuid().ToString("N")[..6].ToUpper()
                    : null)
            .Set(b => b.RejectionReason,
                outcome == "rejected"
                    ? "Room became unavailable during processing."
                    : null)
            .Set(b => b.Events,
                new List<BookingEvent>
                {
                    new("api → POST /bookings received",        Done: true, Time: now.ToString("HH:mm:ss"), Current: false),
                    new("api → published to bookings.created",  Done: true, Time: now.AddMilliseconds(50).ToString("HH:mm:ss"), Current: false),
                    new("worker → message delivered",           Done: true, Time: now.AddMilliseconds(750).ToString("HH:mm:ss"), Current: false),
                    new("worker → reservation locked in mongo", Done: true, Time: now.AddMilliseconds(1550).ToString("HH:mm:ss"), Current: false),
                    new($"worker → status: {outcome}",         Done: true, Time: timeStr, Current: false),
                });

        await col.UpdateOneAsync(filter, update, cancellationToken: ct);
        _logger.LogInformation("[worker] Booking {Id} → {Outcome}", msg.BookingId, outcome);
    }

    private static async Task PatchEventsAsync(
        IMongoCollection<Booking> col, string bookingId, int completedThroughIndex, DateTime baseTime, CancellationToken ct)
    {
        var booking = await col.Find(b => b.Id == bookingId).FirstOrDefaultAsync(ct);
        if (booking is null) return;

        var firstIncompleteIndex = completedThroughIndex + 1;
        var nextCurrentEventIndex = firstIncompleteIndex < booking.Events.Count ? firstIncompleteIndex : NoCurrentEvent;
        var events = booking.Events.Select((e, i) => i <= completedThroughIndex
            ? e with { Done = true, Current = false, Time = e.Time ?? baseTime.AddMilliseconds(i * 700).ToString("HH:mm:ss") }
            : i == nextCurrentEventIndex
                ? e with { Current = true }
                : e).ToList();

        var filter = Builders<Booking>.Filter.Eq(b => b.Id, bookingId);
        var update = Builders<Booking>.Update.Set(b => b.Events, events);
        await col.UpdateOneAsync(filter, update, cancellationToken: ct);
    }
}

// ─── Local models (mirror of API models) ─────────────────────────────────────

internal record BookingMessage(string BookingId, string HotelId, string RoomId, DateTime CreatedAt);

[BsonIgnoreExtraElements]
internal class Booking
{
    [BsonId]
    public string Id { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? ConfirmationCode { get; set; }
    public string? RejectionReason { get; set; }
    public List<BookingEvent> Events { get; set; } = [];
}

internal record BookingEvent(string Label, bool Done, string? Time, bool Current);

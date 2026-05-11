using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace HotelPulse.Worker;

public sealed class BookingConsumer : BackgroundService
{
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
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();

        channel.ExchangeDeclare("hotelpulse", ExchangeType.Topic, durable: true);
        channel.QueueDeclare("bookings.created", durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind("bookings.created", "hotelpulse", "booking.created");
        channel.BasicQos(0, 1, false); // process one message at a time

        var tcs = new TaskCompletionSource();
        ct.Register(() => tcs.TrySetCanceled());

        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += async (_, ea) =>
        {
            try
            {
                var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                var msg = JsonSerializer.Deserialize<BookingMessage>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (msg is null) { channel.BasicNack(ea.DeliveryTag, false, false); return; }

                _logger.LogInformation("[worker] Processing booking {Id}", msg.BookingId);
                await ProcessBookingAsync(bookings, msg, ct);

                channel.BasicAck(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                channel.BasicNack(ea.DeliveryTag, false, requeue: false);
            }
        };

        channel.BasicConsume("bookings.created", autoAck: false, consumer);
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
        await PatchEventsAsync(col, msg.BookingId, 1, now, ct);

        // Step 2 – lock reservation in Mongo (1500ms)
        await Task.Delay(800, ct);
        await PatchEventsAsync(col, msg.BookingId, 2, now, ct);

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
        IMongoCollection<Booking> col, string bookingId, int doneUpTo, DateTime baseTime, CancellationToken ct)
    {
        var booking = await col.Find(b => b.Id == bookingId).FirstOrDefaultAsync(ct);
        if (booking is null) return;

        var events = booking.Events.Select((e, i) => i < doneUpTo
            ? e with { Done = true, Current = false, Time = e.Time ?? baseTime.AddMilliseconds(i * 700).ToString("HH:mm:ss") }
            : i == doneUpTo
                ? e with { Current = true }
                : e).ToList();

        var filter = Builders<Booking>.Filter.Eq(b => b.Id, bookingId);
        var update = Builders<Booking>.Update.Set(b => b.Events, events);
        await col.UpdateOneAsync(filter, update, cancellationToken: ct);
    }
}

// ─── Local models (mirror of API models) ─────────────────────────────────────

record BookingMessage(string BookingId, string HotelId, string RoomId, DateTime CreatedAt);

record BookingEvent(string Label, bool Done, string? Time, bool Current);

class Booking
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? ConfirmationCode { get; set; }
    public string? RejectionReason { get; set; }
    public List<BookingEvent> Events { get; set; } = [];
}

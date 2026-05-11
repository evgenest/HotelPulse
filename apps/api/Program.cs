using HotelPulse.Api.Messaging;
using HotelPulse.Api.Models;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;
using RabbitMQ.Client;
using System.Text.Json;
using System.Text.Json.Serialization;

// MongoDB: use camelCase for all field names
ConventionRegistry.Register(
    "camelCase",
    new ConventionPack { new CamelCaseElementNameConvention() },
    _ => true
);

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(opt =>
{
    opt.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    opt.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// MongoDB
var mongoUri = Environment.GetEnvironmentVariable("MONGO_URI") ?? "mongodb://localhost:27017";
var mongoClient = new MongoClient(mongoUri);
var db = mongoClient.GetDatabase("hotelpulse");
var hotelsCol = db.GetCollection<Hotel>("hotels");
var bookingsCol = db.GetCollection<Booking>("bookings");
builder.Services.AddSingleton(hotelsCol);
builder.Services.AddSingleton(bookingsCol);

// RabbitMQ
var rabbitUri = Environment.GetEnvironmentVariable("RABBITMQ_URI") ?? "amqp://guest:guest@localhost:5672/";
builder.Services.AddSingleton<BookingPublisher>(_ =>
{
    var factory = new ConnectionFactory { Uri = new Uri(rabbitUri) };
    return new BookingPublisher(factory);
});

// CORS – allow frontend origin
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

// Seed hotels on first run
if (!await hotelsCol.Find(_ => true).AnyAsync())
    await hotelsCol.InsertManyAsync(Seed.Hotels);

// ─── Endpoints ────────────────────────────────────────────────────────────────

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

app.MapGet("/api/hotels", async (IMongoCollection<Hotel> col) =>
    await col.Find(_ => true).ToListAsync());

app.MapGet("/api/hotels/{id}", async (string id, IMongoCollection<Hotel> col) =>
{
    var h = await col.Find(x => x.Id == id).FirstOrDefaultAsync();
    return h is null ? Results.NotFound() : Results.Ok(h);
});

app.MapPost("/api/bookings", async (
    CreateBookingRequest req,
    IMongoCollection<Hotel> hotels,
    IMongoCollection<Booking> bookings,
    BookingPublisher publisher) =>
{
    var hotel = await hotels.Find(h => h.Id == req.HotelId).FirstOrDefaultAsync();
    if (hotel is null) return Results.NotFound("Hotel not found");

    var room = hotel.Rooms.FirstOrDefault(r => r.Id == req.RoomId);
    if (room is null) return Results.NotFound("Room not found");

    var id = "bkg_" + Guid.NewGuid().ToString("N")[..12];
    var now = DateTime.UtcNow;
    var timeStr = now.ToString("HH:mm:ss");

    var booking = new Booking
    {
        Id = id,
        HotelId = hotel.Id,
        HotelName = hotel.Name,
        RoomId = room.Id,
        RoomType = room.Type,
        GuestName = req.GuestName,
        CheckIn = req.CheckIn,
        CheckOut = req.CheckOut,
        Nights = req.Nights,
        Total = req.Total,
        Status = "pending",
        CreatedAt = now,
        Events =
        [
            new("api → POST /bookings received",          Done: true,  Time: timeStr, Current: false),
            new("api → published to bookings.created",    Done: false, Time: null,    Current: true),
            new("worker → message delivered",             Done: false, Time: null,    Current: false),
            new("worker → reservation locked in mongo",   Done: false, Time: null,    Current: false),
        ],
    };

    await bookings.InsertOneAsync(booking);

    try { publisher.Publish(new BookingMessage(id, hotel.Id, room.Id, now)); }
    catch (Exception ex) { Console.Error.WriteLine($"[RabbitMQ] Publish failed: {ex.Message}"); }

    return Results.Accepted($"/api/bookings/{id}", new { id, status = "pending" });
});

app.MapGet("/api/bookings/{id}", async (string id, IMongoCollection<Booking> col) =>
{
    var b = await col.Find(x => x.Id == id).FirstOrDefaultAsync();
    return b is null ? Results.NotFound() : Results.Ok(b);
});

app.Run();

// ─── Seed data ────────────────────────────────────────────────────────────────

static class Seed
{
    public static readonly List<Hotel> Hotels =
    [
        new("h_alpina", "Hôtel Alpina", "Zermatt, CH", 5, "ALP-01", 320,
            "Quiet alpine retreat at the foot of the Matterhorn. Rebuilt 2019 with timber and lime plaster. 38 rooms, 2 suites, a small spa, and one excellent restaurant.",
            ["Spa", "Restaurant", "Ski storage", "Sauna", "EV charging", "Pet friendly"],
            [
                new("r1", "Mountain Standard",    2, 320, 22),
                new("r2", "Matterhorn View King", 2, 480, 32),
                new("r3", "Family Loft",          4, 620, 54),
                new("r4", "Alpina Suite",         2, 940, 72),
            ]),

        new("h_marin", "Casa Marin", "Cádiz, ES", 4, "MAR-04", 180,
            "Whitewashed 1920s townhouse a block from the old port. 12 rooms, courtyard breakfast, sea views from the roof. Walking distance to everything.",
            ["Roof terrace", "Breakfast", "Bicycles", "Air-con"],
            [
                new("r1", "Courtyard Single", 1, 180, 16),
                new("r2", "Sea-View Double",  2, 240, 24),
                new("r3", "Top-Floor Studio", 2, 320, 38),
            ]),

        new("h_nordur", "Norður House", "Reykjavík, IS", 4, "NDR-22", 240,
            "Six-room guesthouse in Vesturbær. Walls of board-formed concrete, basalt floors, eider down. A short walk to the harbor and the geothermal pool.",
            ["Geothermal pool access", "Breakfast", "Library"],
            [
                new("r1", "Concrete Standard", 2, 240, 20),
                new("r2", "Harbor Double",     2, 310, 26),
                new("r3", "Eider Suite",       2, 540, 42),
            ]),

        new("h_kiyomi", "Ryokan Kiyomi", "Kyoto, JP", 5, "KYM-08", 410,
            "Eight tatami rooms in Higashiyama, run by the same family since 1973. Kaiseki dinner, cypress-wood bath, silence after 9pm.",
            ["Kaiseki dinner", "Onsen", "Tea ceremony", "Garden"],
            [
                new("r1", "Garden Tatami", 2, 410, 24),
                new("r2", "Cypress Suite", 2, 680, 36),
            ]),

        new("h_porter", "The Porter", "Berlin, DE", 4, "BRL-11", 150,
            "32-room city hotel in Mitte. Concrete floors, vintage Anglepoise lamps, an honest breakfast and a quiet courtyard bar.",
            ["Bar", "Courtyard", "Workspace", "Bicycles", "Air-con"],
            [
                new("r1", "Courtyard Single", 1, 150, 14),
                new("r2", "Standard Queen",   2, 195, 20),
                new("r3", "Corner Studio",    2, 285, 32),
                new("r4", "Apartment",        3, 360, 48),
            ]),

        new("h_solana", "Hotel Solana", "Lisbon, PT", 4, "LIS-19", 200,
            "Tile-fronted 19th-century building in Príncipe Real. 22 rooms, terrazzo bathrooms, an honest wine list, and a small pool on the roof.",
            ["Rooftop pool", "Wine bar", "Breakfast", "Air-con", "Pet friendly"],
            [
                new("r1", "Standard",       2, 200, 22),
                new("r2", "Terrace Double", 2, 285, 28),
                new("r3", "Príncipe Suite", 3, 420, 44),
            ]),
    ];
}

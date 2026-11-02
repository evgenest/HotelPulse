using System.Net.Http.Json;
using System.Text.Json;
using HotelPulse.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace HotelPulse.IntegrationTests;

public sealed class HotelPulseApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
    }
}

[CollectionDefinition("stack")]
public sealed class StackCollection : ICollectionFixture<HotelPulseStack>;

public sealed class HotelPulseStack : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private RedisContainer? _redis;
    private RabbitMqContainer? _rabbit;
    private HotelPulseApiFactory? _factory;
    private IHost? _worker;
    private IConnectionMultiplexer? _cache;

    public HttpClient Client { get; private set; } = null!;
    public string PostgresUri { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var postgres = Environment.GetEnvironmentVariable("POSTGRES_URI");
        var redis = Environment.GetEnvironmentVariable("REDIS_URI");
        var rabbit = Environment.GetEnvironmentVariable("RABBITMQ_URI");

        if (string.IsNullOrWhiteSpace(postgres) || string.IsNullOrWhiteSpace(redis) || string.IsNullOrWhiteSpace(rabbit))
        {
            _postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("hotelpulse")
                .WithUsername("hotelpulse")
                .WithPassword("hotelpulse")
                .Build();
            _redis = new RedisBuilder("redis:7-alpine").Build();
            _rabbit = new RabbitMqBuilder("rabbitmq:4-alpine")
                .WithUsername("hotelpulse")
                .WithPassword("hotelpulse")
                .Build();

            await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _rabbit.StartAsync());
            postgres = _postgres.GetConnectionString();
            redis = _redis.GetConnectionString();
            rabbit = _rabbit.GetConnectionString();
        }

        PostgresUri = postgres;
        var redisUri = RedisAddress.Normalize(redis);
        Environment.SetEnvironmentVariable("POSTGRES_URI", PostgresUri);
        Environment.SetEnvironmentVariable("REDIS_URI", redisUri);
        Environment.SetEnvironmentVariable("RABBITMQ_URI", rabbit);
        Environment.SetEnvironmentVariable("HOTELPULSE_STEP_DELAY_MS", "0");
        Environment.SetEnvironmentVariable("HOTELPULSE_CONFIRM_PROBABILITY", "1");

        _factory = new HotelPulseApiFactory();
        Client = _factory.CreateClient();
        var redisOptions = ConfigurationOptions.Parse(redisUri);
        redisOptions.AllowAdmin = true;
        _cache = await ConnectionMultiplexer.ConnectAsync(redisOptions);
    }

    public async Task DisposeAsync()
    {
        await StopWorkerAsync();
        Client.Dispose();
        _factory?.Dispose();
        _cache?.Dispose();
        if (_rabbit is not null) await _rabbit.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    public async Task ResetAsync()
    {
        await StopWorkerAsync();
        await using var dataSource = NpgsqlDataSource.Create(PostgresUri);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "TRUNCATE outbox, idempotency_keys, bookings RESTART IDENTITY",
            connection);
        await command.ExecuteNonQueryAsync();
        if (_cache is not null)
        {
            foreach (var endpoint in _cache.GetEndPoints())
                await _cache.GetServer(endpoint).FlushDatabaseAsync();
        }
    }

    public async Task StartWorkerAsync()
    {
        if (_worker is not null)
            return;

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddHotelPulse(builder.Configuration, HotelPulseRole.Worker);
        _worker = builder.Build();
        await _worker.StartAsync();
    }

    public async Task StopWorkerAsync()
    {
        if (_worker is null)
            return;

        var worker = _worker;
        _worker = null;
        await worker.StopAsync();
        worker.Dispose();
    }

    public async Task<JsonElement> PostBookingAsync(
        DateOnly checkIn,
        DateOnly checkOut,
        string? idempotencyKey = null,
        string guestName = "Anna Schmidt",
        string hotelId = "h_porter",
        string roomId = "r1",
        int? nights = null,
        decimal? total = null)
    {
        var stayNights = nights ?? checkOut.DayNumber - checkIn.DayNumber;
        var price = roomId == "r2" ? 195m : 150m;
        var body = new
        {
            hotelId,
            roomId,
            guestName,
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            nights = stayNights,
            total = total ?? price * stayNights,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/bookings")
        {
            Content = JsonContent.Create(body),
        };
        if (idempotencyKey is not null)
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        var response = await Client.SendAsync(request);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"POST /api/bookings returned {(int)response.StatusCode}: {json}");

        return json;
    }

    public async Task<HttpResponseMessage> PostRawAsync(object body, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/bookings")
        {
            Content = JsonContent.Create(body),
        };
        if (idempotencyKey is not null)
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return await Client.SendAsync(request);
    }

    public async Task<JsonElement> GetBookingAsync(string id)
    {
        var response = await Client.GetAsync($"/api/bookings/{id}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>())!;
    }

    public async Task<JsonElement> WaitForTerminalAsync(string id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        JsonElement last = default;
        while (DateTime.UtcNow < deadline)
        {
            last = await GetBookingAsync(id);
            var status = last.GetProperty("status").GetString();
            if (status is "confirmed" or "rejected")
                return last;
            await Task.Delay(50);
        }

        throw new TimeoutException($"Booking {id} stayed {last.GetProperty("status").GetString()}.");
    }

    public async Task<string> ScalarAsync(string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresUri);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value?.ToString() ?? "";
    }
}

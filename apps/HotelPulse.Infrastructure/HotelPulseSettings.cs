using System.Globalization;
using HotelPulse.Domain;
using Microsoft.Extensions.Configuration;

namespace HotelPulse.Infrastructure;

public sealed record HotelPulseSettings(
    string PostgresUri,
    string RedisUri,
    string RabbitMqUri,
    int[] StepDelayMs,
    double ConfirmProbability,
    bool RunWorkerInProcess,
    int BookingRateLimitPerMinute = HotelPulseSettings.DefaultBookingRateLimit)
{
    /// <summary>Booking requests per client IP per minute; 0 turns the limit off.</summary>
    public const int DefaultBookingRateLimit = 30;

    public static HotelPulseSettings From(IConfiguration configuration)
    {
        var postgresRaw = First(configuration["DATABASE_URL"], configuration["POSTGRES_URI"])
            ?? "Host=localhost;Port=5432;Database=hotelpulse;Username=hotelpulse;Password=hotelpulse";
        var redis = First(configuration["REDIS_URL"], configuration["REDIS_URI"]) ?? "localhost:6379";
        var rabbit = First(configuration["AMQP_URL"], configuration["RABBITMQ_URI"])
            ?? "amqp://guest:guest@localhost:5672/";
        var delays = StepDelays.Resolve(configuration["HOTELPULSE_STEP_DELAY_MS"]);
        var maxPool = PostgresConnection.DefaultMaxPoolSize;
        if (int.TryParse(configuration["POSTGRES_MAX_POOL_SIZE"], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPool)
            && parsedPool > 0)
        {
            maxPool = parsedPool;
        }

        var postgres = PostgresConnection.ToNpgsql(postgresRaw, configuration["POSTGRES_SSL_MODE"], maxPool);

        var probability = 0.9;
        var rawProbability = configuration["HOTELPULSE_CONFIRM_PROBABILITY"];
        if (!string.IsNullOrWhiteSpace(rawProbability)
            && double.TryParse(rawProbability, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            probability = Math.Clamp(parsed, 0, 1);
        }

        return new HotelPulseSettings(
            postgres,
            redis,
            rabbit,
            delays,
            probability,
            Flag.IsEnabled(configuration["RUN_WORKER_IN_PROCESS"]),
            int.TryParse(configuration["HOTELPULSE_BOOKING_RATE_LIMIT"], NumberStyles.None, CultureInfo.InvariantCulture, out var limit)
                ? limit
                : DefaultBookingRateLimit);
    }

    private static string? First(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}

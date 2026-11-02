using System.Globalization;
using HotelPulse.Domain;
using Microsoft.Extensions.Configuration;

namespace HotelPulse.Infrastructure;

public sealed record HotelPulseSettings(
    string PostgresUri,
    string RedisUri,
    string RabbitMqUri,
    int[] StepDelayMs,
    double ConfirmProbability)
{
    public static HotelPulseSettings From(IConfiguration configuration)
    {
        var postgres = configuration["POSTGRES_URI"]
            ?? "Host=localhost;Port=5432;Database=hotelpulse;Username=hotelpulse;Password=hotelpulse";
        var redis = RedisAddress.Normalize(configuration["REDIS_URI"] ?? "localhost:6379");
        var rabbit = configuration["RABBITMQ_URI"] ?? "amqp://guest:guest@localhost:5672/";
        var delays = StepDelays.Resolve(configuration["HOTELPULSE_STEP_DELAY_MS"]);

        var probability = 0.9;
        var rawProbability = configuration["HOTELPULSE_CONFIRM_PROBABILITY"];
        if (!string.IsNullOrWhiteSpace(rawProbability)
            && double.TryParse(rawProbability, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            probability = Math.Clamp(parsed, 0, 1);
        }

        return new HotelPulseSettings(postgres, redis, rabbit, delays, probability);
    }
}

public static class RedisAddress
{
    public static string Normalize(string value)
    {
        if (!value.StartsWith("redis://", StringComparison.OrdinalIgnoreCase))
            return value;

        var uri = new Uri(value);
        return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
    }
}

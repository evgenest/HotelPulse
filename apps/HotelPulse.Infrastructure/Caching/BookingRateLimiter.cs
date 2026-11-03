using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HotelPulse.Infrastructure.Caching;

public enum RateLimitDecision
{
    Allowed,
    Limited,
    SkippedRedisDown,
}

/// <summary>
/// At most N booking requests per minute per client, counted in Redis so all
/// API copies share the count. If Redis is down the request goes through: a
/// cache outage must never fail a request (CLAUDE.md), and that rule wins over
/// the limit (ADR-0013). Same behaviour as the Laravel RateLimitBookings.
/// </summary>
public sealed class BookingRateLimiter(IConnectionMultiplexer redis, HotelPulseSettings settings, ILogger<BookingRateLimiter> logger)
{
    public int Limit => settings.BookingRateLimitPerMinute;

    public async Task<RateLimitDecision> CheckAsync(string client, DateTimeOffset now)
    {
        if (Limit <= 0)
            return RateLimitDecision.Allowed;

        var window = now.ToUnixTimeSeconds() / 60;
        var key = $"hp:ratelimit:bookings:{Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(client)))}:{window}";
        try
        {
            var db = redis.GetDatabase();
            var count = await db.StringIncrementAsync(key);
            if (count == 1)
                await db.KeyExpireAsync(key, TimeSpan.FromSeconds(120));
            return count > Limit ? RateLimitDecision.Limited : RateLimitDecision.Allowed;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Rate limit skipped: Redis unavailable");
            return RateLimitDecision.SkippedRedisDown;
        }
    }

    public static int SecondsUntilNextWindow(DateTimeOffset now) => 60 - (int)(now.ToUnixTimeSeconds() % 60);
}

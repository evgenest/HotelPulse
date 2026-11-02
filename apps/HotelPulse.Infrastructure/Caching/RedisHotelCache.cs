using System.Text.Json;
using HotelPulse.Domain;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HotelPulse.Infrastructure.Caching;

public sealed class RedisHotelCache(IConnectionMultiplexer redis, ILogger<RedisHotelCache> logger)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    public async Task<long?> VersionAsync(string key)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(key);
            return value.IsNullOrEmpty ? 0 : (long)value;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis version read failed for {Key}", key);
            return null;
        }
    }

    public async Task<(bool Ok, T? Value)> TryGetAsync<T>(string key)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(key);
            if (value.IsNullOrEmpty)
                return (true, default);

            return (true, JsonSerializer.Deserialize<T>(value.ToString(), HotelPulseJson.Serializer));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis read failed for {Key}", key);
            return (false, default);
        }
    }

    public async Task SetAsync<T>(string key, T value)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, HotelPulseJson.Serializer);
            await redis.GetDatabase().StringSetAsync(key, json, Ttl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis write failed for {Key}", key);
        }
    }

    public async Task InvalidateHotelAsync(string hotelId)
    {
        try
        {
            var db = redis.GetDatabase();
            var batch = db.CreateBatch();
            var catalog = batch.StringIncrementAsync(CacheKeys.CatalogVersion);
            var hotel = batch.StringIncrementAsync(CacheKeys.HotelVersion(hotelId));
            batch.Execute();
            await Task.WhenAll(catalog, hotel);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache invalidation failed for {HotelId}", hotelId);
        }
    }
}

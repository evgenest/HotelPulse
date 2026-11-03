using HotelPulse.Domain;
using HotelPulse.Infrastructure.Postgres;

namespace HotelPulse.Infrastructure.Caching;

public sealed class HotelCatalog(HotelRepository hotels, RedisHotelCache cache)
{
    public async Task<CachedResult<IReadOnlyList<Hotel>>> ListAsync(CancellationToken ct)
    {
        var version = await cache.VersionAsync(CacheKeys.CatalogVersion);
        if (version is null)
            return new(await hotels.ListAsync(ct), "BYPASS");

        var key = CacheKeys.HotelList(version.Value);
        var cached = await cache.TryGetAsync<List<Hotel>>(key);
        if (!cached.Ok)
            return new(await hotels.ListAsync(ct), "BYPASS");
        if (cached.Value is not null)
            return new(cached.Value, "HIT");

        var rows = await hotels.ListAsync(ct);
        await cache.SetAsync(key, rows);
        return new(rows, "MISS");
    }

    public async Task<CachedResult<Hotel>?> GetAsync(string id, CancellationToken ct)
    {
        var version = await cache.VersionAsync(CacheKeys.HotelVersion(id));
        if (version is null)
        {
            var direct = await hotels.GetAsync(id, ct);
            return direct is null ? null : new CachedResult<Hotel>(direct, "BYPASS");
        }

        var key = CacheKeys.Hotel(id, version.Value);
        var cached = await cache.TryGetAsync<Hotel>(key);
        if (cached.Ok && cached.Value is not null)
            return new(cached.Value, "HIT");

        var hotel = await hotels.GetAsync(id, ct);
        if (hotel is null)
            return null;

        if (cached.Ok)
            await cache.SetAsync(key, hotel);

        return new(hotel, cached.Ok ? "MISS" : "BYPASS");
    }

    public async Task<CachedResult<IReadOnlyList<RoomAvailability>>?> AvailabilityAsync(
        string hotelId,
        DateOnly checkIn,
        DateOnly checkOut,
        CancellationToken ct)
    {
        var version = await cache.VersionAsync(CacheKeys.HotelVersion(hotelId));
        if (version is null)
        {
            var direct = await hotels.AvailabilityAsync(hotelId, checkIn, checkOut, ct);
            if (direct.Count == 0 && await hotels.GetAsync(hotelId, ct) is null)
                return null;
            return new(direct, "BYPASS");
        }

        var key = CacheKeys.Availability(hotelId, version.Value, checkIn, checkOut);
        var cached = await cache.TryGetAsync<List<RoomAvailability>>(key);
        if (cached.Ok && cached.Value is not null)
            return new(cached.Value, "HIT");

        var hotel = await hotels.GetAsync(hotelId, ct);
        if (hotel is null)
            return null;

        var rows = await hotels.AvailabilityAsync(hotelId, checkIn, checkOut, ct);
        if (cached.Ok)
            await cache.SetAsync(key, rows);

        return new(rows, cached.Ok ? "MISS" : "BYPASS");
    }
}

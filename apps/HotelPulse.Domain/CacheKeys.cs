namespace HotelPulse.Domain;

public static class CacheKeys
{
    public const string CatalogVersion = "hp:ver:catalog";

    public static string HotelVersion(string hotelId) => $"hp:ver:hotel:{hotelId}";

    public static string HotelList(long version) => $"hp:hotels:v{version}";

    public static string Hotel(string id, long version) => $"hp:hotels:{id}:v{version}";

    public static string Availability(string hotelId, long version, DateOnly checkIn, DateOnly checkOut) =>
        $"hp:avail:{hotelId}:v{version}:{checkIn:yyyy-MM-dd}:{checkOut:yyyy-MM-dd}";
}

using System.Text.Json;
using Dapper;
using HotelPulse.Domain;
using Npgsql;

namespace HotelPulse.Infrastructure.Postgres;

public sealed class HotelRepository(NpgsqlDataSource dataSource)
{
    private const string HotelSql =
        """
        SELECT h.id,
               h.name,
               h.city,
               h.rating,
               h.glyph,
               h.price_from,
               h.description,
               h.amenities,
               COALESCE(rooms.rooms, '[]'::jsonb)::text AS rooms
        FROM hotels h
        LEFT JOIN LATERAL (
            SELECT jsonb_agg(
                       jsonb_build_object(
                           'id', room.id,
                           'type', room.type,
                           'capacity', room.capacity,
                           'price', room.price,
                           'sqm', room.sqm
                       )
                       ORDER BY room.price, room.id
                   ) AS rooms
            FROM rooms room
            WHERE room.hotel_id = h.id
        ) rooms ON true
        """;

    public async Task<IReadOnlyList<Hotel>> ListAsync(CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<HotelRow>(HotelSql + " ORDER BY h.name");
        return rows.Select(Map).ToArray();
    }

    public async Task<Hotel?> GetAsync(string id, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<HotelRow>(
            HotelSql + " WHERE h.id = @id",
            new { id });
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<RoomAvailability>> AvailabilityAsync(
        string hotelId,
        DateOnly checkIn,
        DateOnly checkOut,
        CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<RoomAvailability>(
            AvailabilitySql.Query,
            new { hotelId, checkIn, checkOut });
        return rows.ToArray();
    }

    private static Hotel Map(HotelRow row)
    {
        var rooms = JsonSerializer.Deserialize<List<Room>>(row.Rooms, HotelPulseJson.Serializer) ?? [];
        return new Hotel(
            row.Id,
            row.Name,
            row.City,
            row.Rating,
            row.Glyph,
            row.PriceFrom,
            row.Description,
            row.Amenities ?? [],
            rooms);
    }

    private sealed class HotelRow
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string City { get; set; } = "";
        public int Rating { get; set; }
        public string Glyph { get; set; } = "";
        public decimal PriceFrom { get; set; }
        public string Description { get; set; } = "";
        public string[]? Amenities { get; set; }
        public string Rooms { get; set; } = "[]";
    }
}

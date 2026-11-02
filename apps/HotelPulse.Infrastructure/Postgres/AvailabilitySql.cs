namespace HotelPulse.Infrastructure.Postgres;

/// <summary>
/// One indexed lookup per room. See docs/sql.md for the plan and why the
/// overlap predicate is written as two inequalities.
/// </summary>
public static class AvailabilitySql
{
    public const string Query =
        """
        SELECT r.id,
               r.type,
               r.capacity,
               r.price,
               r.sqm,
               NOT EXISTS (
                   SELECT 1
                   FROM bookings b
                   WHERE b.hotel_id = r.hotel_id
                     AND b.room_id = r.id
                     AND b.status IN ('pending', 'confirmed')
                     AND b.check_in < @checkOut
                     AND b.check_out > @checkIn
               ) AS available
        FROM rooms r
        WHERE r.hotel_id = @hotelId
        ORDER BY r.price, r.id
        """;
}

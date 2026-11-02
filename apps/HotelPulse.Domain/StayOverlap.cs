namespace HotelPulse.Domain;

/// <summary>
/// Half-open stay overlap: a checkout morning does not collide with the next check-in.
/// The PostgreSQL exclusion constraint uses the same predicate,
/// <c>daterange(check_in, check_out, '[)') &amp;&amp; daterange(...)</c>.
/// </summary>
public static class StayOverlap
{
    public static bool Overlaps(DateOnly checkIn, DateOnly checkOut, DateOnly otherCheckIn, DateOnly otherCheckOut) =>
        checkIn < otherCheckOut && checkOut > otherCheckIn;
}

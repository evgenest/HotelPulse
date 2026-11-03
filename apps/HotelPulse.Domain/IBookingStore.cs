namespace HotelPulse.Domain;

public interface IBookingStore
{
    Task<Booking?> FindAsync(string id, CancellationToken ct);

    Task UpdateEventsAsync(string id, IReadOnlyList<BookingEvent> events, CancellationToken ct);

    /// <summary>
    /// Compare-and-set from pending. Returns false when the row was already terminal.
    /// </summary>
    Task<bool> TryCompleteAsync(string id, BookingCompletion completion, CancellationToken ct);
}

public interface IBookingCommands
{
    Task<CreateBookingResult> CreateAsync(Booking booking, string? idempotencyKey, string requestHash, CancellationToken ct);

    Task<IReadOnlyList<Booking>> ListRecentAsync(int limit, CancellationToken ct);

    Task<IReadOnlyList<Booking>> GetManyAsync(IReadOnlyList<string> ids, CancellationToken ct);

    /// <summary>
    /// Sets the booking to cancelled if it is still pending or confirmed and
    /// check-in is after <paramref name="today"/>. Returns the updated booking,
    /// or null when the row no longer matches those conditions.
    /// </summary>
    Task<Booking?> TryCancelAsync(string id, DateOnly today, IReadOnlyList<BookingEvent> events, CancellationToken ct);
}

public sealed record CreateBookingResult(Booking Booking, bool Replay);

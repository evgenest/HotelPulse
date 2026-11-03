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
}

public sealed record CreateBookingResult(Booking Booking, bool Replay);

namespace HotelPulse.Domain;

public enum CancellationDecision
{
    Cancel,
    AlreadyCancelled,
    NotCancellable,
    TooLate,
}

/// <summary>
/// The rules from docs/specs/booking-cancellation.md. The SQL update repeats
/// them in its WHERE clause, so a concurrent change between the read and the
/// write cannot slip through.
/// </summary>
public static class BookingCancellation
{
    public const string RejectedError = "A rejected booking cannot be cancelled.";
    public const string TooLateError = "Bookings can only be cancelled before the check-in date.";

    public static CancellationDecision Decide(string status, DateOnly checkIn, DateOnly today) => status switch
    {
        BookingStatuses.Cancelled => CancellationDecision.AlreadyCancelled,
        BookingStatuses.Rejected => CancellationDecision.NotCancellable,
        BookingStatuses.Pending or BookingStatuses.Confirmed when checkIn <= today => CancellationDecision.TooLate,
        BookingStatuses.Pending or BookingStatuses.Confirmed => CancellationDecision.Cancel,
        _ => CancellationDecision.NotCancellable,
    };

    public static string? Error(CancellationDecision decision) => decision switch
    {
        CancellationDecision.NotCancellable => RejectedError,
        CancellationDecision.TooLate => TooLateError,
        _ => null,
    };
}

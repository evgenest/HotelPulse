namespace HotelPulse.Domain;

public static class BookingStatuses
{
    public const string Pending = "pending";
    public const string Confirmed = "confirmed";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";

    public const string UnavailableReason = "Room became unavailable during processing.";
    public const string ProcessingFailedReason = "Processing failed after a retry. The stay was not confirmed.";

    /// <summary>
    /// Terminal for the worker: it never moves a booking out of these.
    /// A confirmed booking can still be cancelled by the guest; see <see cref="BookingCancellation"/>.
    /// </summary>
    public static bool IsTerminal(string status) => status is Confirmed or Rejected or Cancelled;

    public static bool HoldsInventory(string status) => status is Pending or Confirmed;

    /// <summary>
    /// The only forward moves are pending → confirmed and pending → rejected.
    /// A second delivery of the same message is not a transition; the worker skips it.
    /// </summary>
    public static bool IsLegalTransition(string from, string to) =>
        from == Pending && to is Confirmed or Rejected;
}

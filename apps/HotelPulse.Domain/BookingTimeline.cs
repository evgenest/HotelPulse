namespace HotelPulse.Domain;

public static class BookingLabels
{
    public const string Received = "api → POST /bookings received";
    public const string Published = "api → published to bookings.created";
    public const string Delivered = "worker → message delivered";
    public const string Locked = "worker → reservation locked in postgres";
    public const string Cancelled = "api → booking cancelled";

    public static string Status(string outcome) => $"worker → status: {outcome}";
}

public static class BookingTimeline
{
    public const int Received = 0;
    public const int Published = 1;
    public const int Delivered = 2;
    public const int Locked = 3;

    public static IReadOnlyList<BookingEvent> Initial(DateTime utcNow) =>
    [
        new(BookingLabels.Received, true, Format(utcNow), false),
        new(BookingLabels.Published, false, null, true),
        new(BookingLabels.Delivered, false, null, false),
        new(BookingLabels.Locked, false, null, false),
    ];

    public static IReadOnlyList<BookingEvent> Advance(IReadOnlyList<BookingEvent> events, int completedThrough, DateTime utcNow)
    {
        var next = completedThrough + 1;
        return events.Select((e, i) =>
        {
            if (i <= completedThrough)
                return e with { Done = true, Current = false, Time = e.Time ?? Format(utcNow.AddMilliseconds(i * 700)) };
            if (i == next)
                return e with { Done = false, Current = true };
            return e with { Current = false };
        }).ToArray();
    }

    public static IReadOnlyList<BookingEvent> Terminal(DateTime startedAt, DateTime completedAt, string outcome) =>
    [
        new(BookingLabels.Received, true, Format(startedAt), false),
        new(BookingLabels.Published, true, Format(startedAt.AddMilliseconds(50)), false),
        new(BookingLabels.Delivered, true, Format(startedAt.AddMilliseconds(750)), false),
        new(BookingLabels.Locked, true, Format(startedAt.AddMilliseconds(1550)), false),
        new(BookingLabels.Status(outcome), true, Format(completedAt), false),
    ];

    public static IReadOnlyList<BookingEvent> Cancelled(IReadOnlyList<BookingEvent> events, DateTime utcNow) =>
    [
        .. events.Select(e => e with { Current = false }),
        new(BookingLabels.Cancelled, true, Format(utcNow), false),
    ];

    public static string Format(DateTime utc) => utc.ToString("HH:mm:ss");
}

using HotelPulse.Domain;

namespace HotelPulse.UnitTests;

public class BookingCancellationTests
{
    private static readonly DateOnly Today = new(2026, 11, 3);

    [Theory]
    [InlineData("pending", 1, CancellationDecision.Cancel)]
    [InlineData("confirmed", 1, CancellationDecision.Cancel)]
    [InlineData("confirmed", 30, CancellationDecision.Cancel)]
    [InlineData("pending", 0, CancellationDecision.TooLate)]
    [InlineData("confirmed", 0, CancellationDecision.TooLate)]
    [InlineData("confirmed", -2, CancellationDecision.TooLate)]
    [InlineData("rejected", 5, CancellationDecision.NotCancellable)]
    [InlineData("cancelled", 5, CancellationDecision.AlreadyCancelled)]
    [InlineData("cancelled", -5, CancellationDecision.AlreadyCancelled)]
    public void The_decision_follows_the_spec(string status, int checkInInDays, CancellationDecision expected)
    {
        Assert.Equal(expected, BookingCancellation.Decide(status, Today.AddDays(checkInInDays), Today));
    }

    [Fact]
    public void An_unknown_status_is_not_cancellable()
    {
        Assert.Equal(CancellationDecision.NotCancellable, BookingCancellation.Decide("archived", Today.AddDays(3), Today));
    }

    [Theory]
    [InlineData(CancellationDecision.NotCancellable, BookingCancellation.RejectedError)]
    [InlineData(CancellationDecision.TooLate, BookingCancellation.TooLateError)]
    [InlineData(CancellationDecision.Cancel, null)]
    [InlineData(CancellationDecision.AlreadyCancelled, null)]
    public void Only_refusals_carry_an_error(CancellationDecision decision, string? error)
    {
        Assert.Equal(error, BookingCancellation.Error(decision));
    }

    [Fact]
    public void Cancelling_appends_one_event_and_clears_the_current_marker()
    {
        var now = new DateTime(2026, 11, 3, 10, 15, 0, DateTimeKind.Utc);
        var events = BookingTimeline.Initial(now.AddMinutes(-5));

        var cancelled = BookingTimeline.Cancelled(events, now);

        Assert.Equal(events.Count + 1, cancelled.Count);
        Assert.Equal(BookingLabels.Cancelled, cancelled[^1].Label);
        Assert.True(cancelled[^1].Done);
        Assert.Equal("10:15:00", cancelled[^1].Time);
        Assert.DoesNotContain(cancelled, e => e.Current);
    }
}

using HotelPulse.Domain;

namespace HotelPulse.UnitTests;

public class BookingStatusTests
{
    [Theory]
    [InlineData("pending", "confirmed", true)]
    [InlineData("pending", "rejected", true)]
    [InlineData("confirmed", "confirmed", false)]
    [InlineData("confirmed", "rejected", false)]
    [InlineData("rejected", "confirmed", false)]
    [InlineData("rejected", "pending", false)]
    [InlineData("pending", "cancelled", false)]
    [InlineData("cancelled", "confirmed", false)]
    public void Only_pending_moves_to_a_terminal_status(string from, string to, bool legal)
    {
        Assert.Equal(legal, BookingStatuses.IsLegalTransition(from, to));
    }

    [Theory]
    [InlineData("pending", false)]
    [InlineData("confirmed", true)]
    [InlineData("rejected", true)]
    [InlineData("cancelled", true)]
    public void Terminal_statuses_are_confirmed_rejected_and_cancelled(string status, bool terminal)
    {
        Assert.Equal(terminal, BookingStatuses.IsTerminal(status));
    }

    [Theory]
    [InlineData("pending", true)]
    [InlineData("confirmed", true)]
    [InlineData("rejected", false)]
    [InlineData("cancelled", false)]
    public void Rejected_stays_do_not_hold_the_room(string status, bool holds)
    {
        Assert.Equal(holds, BookingStatuses.HoldsInventory(status));
    }
}

public class StayOverlapTests
{
    private static readonly DateOnly First = new(2026, 7, 1);

    [Fact]
    public void Overlapping_ranges_collide()
    {
        Assert.True(StayOverlap.Overlaps(First, First.AddDays(4), First.AddDays(3), First.AddDays(6)));
    }

    [Fact]
    public void Checkout_morning_is_free_for_the_next_guest()
    {
        Assert.False(StayOverlap.Overlaps(First, First.AddDays(4), First.AddDays(4), First.AddDays(6)));
    }

    [Fact]
    public void A_stay_entirely_before_another_does_not_collide()
    {
        Assert.False(StayOverlap.Overlaps(First, First.AddDays(2), First.AddDays(3), First.AddDays(5)));
    }

    [Fact]
    public void Containment_is_an_overlap()
    {
        Assert.True(StayOverlap.Overlaps(First, First.AddDays(10), First.AddDays(2), First.AddDays(3)));
    }
}

public class BookingValidatorTests
{
    private static readonly DateOnly Today = new(2026, 6, 1);
    private static readonly Room Room = new("r1", "Standard", 2, 100m, 20);

    [Fact]
    public void Accepts_a_stay_whose_nights_and_total_match_the_room()
    {
        var errors = BookingValidator.Validate(Command(nights: 3, total: 300m), Room, Today);
        Assert.Empty(errors);
    }

    [Fact]
    public void Rejects_a_blank_guest_name()
    {
        var command = Command() with { GuestName = "   " };
        Assert.Contains(BookingValidator.Validate(command, Room, Today), error => error.Field == "guestName");
    }

    [Fact]
    public void Rejects_a_guest_name_over_120_characters()
    {
        var command = Command() with { GuestName = new string('a', 121) };
        Assert.Contains(BookingValidator.Validate(command, Room, Today), error => error.Field == "guestName");
    }

    [Fact]
    public void Rejects_checkout_on_or_before_checkin()
    {
        var command = Command() with { CheckOut = "2026-07-01" };
        Assert.Contains(BookingValidator.Validate(command, Room, Today), error => error.Field == "checkOut");
    }

    [Fact]
    public void Rejects_a_night_count_that_does_not_match_the_dates()
    {
        var errors = BookingValidator.Validate(Command(nights: 2, total: 200m), Room, Today);
        Assert.Contains(errors, error => error.Field == "nights");
    }

    [Fact]
    public void Rejects_a_total_that_does_not_match_price_times_nights()
    {
        var errors = BookingValidator.Validate(Command(nights: 3, total: 250m), Room, Today);
        Assert.Contains(errors, error => error.Field == "total");
    }

    [Fact]
    public void Rejects_a_checkin_in_the_past()
    {
        var command = Command() with { CheckIn = "2026-05-01", CheckOut = "2026-05-03", Nights = 2, Total = 200m };
        Assert.Contains(BookingValidator.Validate(command, Room, Today), error => error.Field == "checkIn");
    }

    [Fact]
    public void Rejects_dates_that_are_not_iso()
    {
        var command = Command() with { CheckIn = "01-07-2026" };
        Assert.Contains(BookingValidator.Validate(command, Room, Today), error => error.Field == "checkIn");
    }

    [Fact]
    public void Trims_the_guest_name_before_measuring_it()
    {
        var command = Command() with { GuestName = "  Anna Schmidt  " };
        Assert.Empty(BookingValidator.Validate(command, Room, Today));
    }

    private static CreateBookingCommand Command(int nights = 3, decimal total = 300m) => new(
        "h_porter",
        "r1",
        "Anna Schmidt",
        "2026-07-01",
        "2026-07-04",
        nights,
        total);
}

public class BookingTimelineTests
{
    private static readonly DateTime At = new(2026, 7, 1, 14, 32, 0, DateTimeKind.Utc);

    [Fact]
    public void A_new_booking_has_received_the_request_and_is_waiting_to_publish()
    {
        var events = BookingTimeline.Initial(At);

        Assert.Equal(4, events.Count);
        Assert.True(events[0].Done);
        Assert.False(events[0].Current);
        Assert.False(events[1].Done);
        Assert.True(events[1].Current);
        Assert.Equal(BookingLabels.Locked, events[3].Label);
    }

    [Fact]
    public void Advancing_to_delivered_marks_the_publish_step_done()
    {
        var events = BookingTimeline.Advance(BookingTimeline.Initial(At), BookingTimeline.Delivered, At.AddSeconds(1));

        Assert.All(events.Take(3), step => Assert.True(step.Done));
        Assert.True(events[3].Current);
        Assert.False(events[3].Done);
    }

    [Fact]
    public void Advancing_to_locked_leaves_no_current_step()
    {
        var delivered = BookingTimeline.Advance(BookingTimeline.Initial(At), BookingTimeline.Delivered, At);
        var locked = BookingTimeline.Advance(delivered, BookingTimeline.Locked, At.AddSeconds(1));

        Assert.All(locked, step => Assert.True(step.Done));
        Assert.DoesNotContain(locked, step => step.Current);
    }

    [Fact]
    public void A_second_advance_keeps_completed_steps_done()
    {
        var once = BookingTimeline.Advance(BookingTimeline.Initial(At), BookingTimeline.Delivered, At);
        var twice = BookingTimeline.Advance(once, BookingTimeline.Delivered, At.AddMinutes(1));

        Assert.Equal(once.Select(step => step.Done), twice.Select(step => step.Done));
        Assert.Equal(once[0].Time, twice[0].Time);
    }

    [Fact]
    public void The_terminal_timeline_records_the_outcome()
    {
        var events = BookingTimeline.Terminal(At, At.AddSeconds(3), BookingStatuses.Confirmed);

        Assert.Equal(5, events.Count);
        Assert.All(events, step => Assert.True(step.Done));
        Assert.Equal(BookingLabels.Status("confirmed"), events[^1].Label);
    }
}

public class BookingOutcomeTests
{
    [Theory]
    [InlineData(0.0, 0.9, "confirmed")]
    [InlineData(0.899, 0.9, "confirmed")]
    [InlineData(0.9, 0.9, "rejected")]
    [InlineData(0.999, 1.0, "confirmed")]
    [InlineData(0.0, 0.0, "rejected")]
    public void The_roll_is_confirmed_when_it_is_below_the_probability(double roll, double probability, string status)
    {
        Assert.Equal(status, BookingOutcome.FromRoll(roll, probability));
    }

    [Fact]
    public void A_roll_outside_the_unit_interval_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BookingOutcome.FromRoll(1, 0.9));
    }
}

public class DeliveryPolicyTests
{
    [Theory]
    [InlineData(FailureKind.Permanent, false, QueueAction.Discard, true)]
    [InlineData(FailureKind.Permanent, true, QueueAction.Discard, true)]
    [InlineData(FailureKind.Transient, false, QueueAction.Requeue, false)]
    [InlineData(FailureKind.Transient, true, QueueAction.Discard, true)]
    [InlineData(FailureKind.DatabaseUnavailable, false, QueueAction.Requeue, false)]
    [InlineData(FailureKind.DatabaseUnavailable, true, QueueAction.Requeue, false)]
    public void Failures_are_retried_once_unless_the_database_is_down(
        FailureKind kind,
        bool redelivered,
        QueueAction action,
        bool markRejected)
    {
        var plan = DeliveryPolicy.Decide(kind, redelivered);
        Assert.Equal(action, plan.Action);
        Assert.Equal(markRejected, plan.MarkRejected);
    }
}

public class ConfirmationCodeTests
{
    [Fact]
    public void Codes_use_the_first_six_hex_characters_of_the_guid()
    {
        var code = ConfirmationCodes.Create(Guid.Parse("a3f9c12d-8e4b-4000-8000-000000000000"));
        Assert.Equal("HP-A3F9C1", code);
        Assert.True(ConfirmationCodes.IsWellFormed(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("HP-a3f9c1")]
    [InlineData("HP-A3F9")]
    [InlineData("XX-A3F9C1")]
    public void Malformed_codes_are_rejected(string? code)
    {
        Assert.False(ConfirmationCodes.IsWellFormed(code));
    }
}

public class IdempotencyHashTests
{
    [Fact]
    public void The_same_request_hashes_to_the_same_value()
    {
        var left = Sample();
        var right = Sample() with { GuestName = "  Anna Schmidt " };
        Assert.Equal(IdempotencyHash.Compute(left), IdempotencyHash.Compute(right));
    }

    [Fact]
    public void A_different_guest_is_a_different_request()
    {
        var left = IdempotencyHash.Compute(Sample());
        var right = IdempotencyHash.Compute(Sample() with { GuestName = "Boris" });
        Assert.NotEqual(left, right);
    }

    private static CreateBookingCommand Sample() => new(
        "h_porter", "r1", "Anna Schmidt", "2026-07-01", "2026-07-04", 3, 450m);
}

public class StepDelayTests
{
    [Fact]
    public void The_product_pipeline_keeps_the_original_pacing()
    {
        Assert.Equal([700, 800, 900], StepDelays.Resolve(null));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("15")]
    public void An_explicit_delay_replaces_every_step(string value)
    {
        var expected = int.Parse(value);
        Assert.Equal([expected, expected, expected], StepDelays.Resolve(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("fast")]
    [InlineData("-1")]
    public void Invalid_overrides_fall_back_to_the_product_pacing(string value)
    {
        Assert.Equal(StepDelays.Default, StepDelays.Resolve(value));
    }
}

public class CacheKeyTests
{
    [Fact]
    public void A_version_bump_changes_the_key_so_the_previous_entry_is_unused()
    {
        Assert.NotEqual(CacheKeys.HotelList(1), CacheKeys.HotelList(2));
        Assert.Equal(
            "hp:avail:h_porter:v4:2026-07-01:2026-07-04",
            CacheKeys.Availability("h_porter", 4, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 4)));
    }
}

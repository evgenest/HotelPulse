using HotelPulse.Domain;

namespace HotelPulse.UnitTests;

public class BookingProcessorTests
{
    private static readonly DateTime Now = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_pending_booking_is_confirmed_and_given_a_code()
    {
        var store = Store();
        var processor = Processor(store, BookingStatuses.Confirmed);

        var result = await processor.ProcessAsync(Message(), CancellationToken.None);

        var saved = store.Items["bkg_test"];
        Assert.IsType<ProcessingResult.Completed>(result);
        Assert.Equal(BookingStatuses.Confirmed, saved.Status);
        Assert.True(ConfirmationCodes.IsWellFormed(saved.ConfirmationCode));
        Assert.Null(saved.RejectionReason);
        Assert.Equal(BookingLabels.Status("confirmed"), saved.Events[^1].Label);
        Assert.All(saved.Events, step => Assert.True(step.Done));
    }

    [Fact]
    public async Task A_rejected_outcome_stores_the_reason_and_no_code()
    {
        var store = Store();
        var processor = Processor(store, BookingStatuses.Rejected);

        await processor.ProcessAsync(Message(), CancellationToken.None);

        var saved = store.Items["bkg_test"];
        Assert.Equal(BookingStatuses.Rejected, saved.Status);
        Assert.Null(saved.ConfirmationCode);
        Assert.Equal(BookingStatuses.UnavailableReason, saved.RejectionReason);
    }

    [Fact]
    public async Task An_already_confirmed_booking_is_not_written_again()
    {
        var store = Store(BookingStatuses.Confirmed, "HP-ABCDEF");
        var processor = Processor(store, BookingStatuses.Rejected);

        var result = await processor.ProcessAsync(Message(), CancellationToken.None);

        Assert.IsType<ProcessingResult.Skipped>(result);
        Assert.Equal(0, store.EventWrites);
        Assert.Equal(0, store.Completes);
        Assert.Equal("HP-ABCDEF", store.Items["bkg_test"].ConfirmationCode);
    }

    [Fact]
    public async Task A_lost_compare_and_set_is_treated_as_already_handled()
    {
        var store = Store();
        store.FailNextComplete = true;
        var processor = Processor(store, BookingStatuses.Confirmed);

        var result = await processor.ProcessAsync(Message(), CancellationToken.None);

        Assert.IsType<ProcessingResult.Skipped>(result);
        Assert.Equal(BookingStatuses.Pending, store.Items["bkg_test"].Status);
    }

    [Fact]
    public async Task A_missing_booking_is_a_permanent_failure()
    {
        var processor = Processor(new InMemoryBookingStore(), BookingStatuses.Confirmed);
        await Assert.ThrowsAsync<PermanentProcessingException>(() => processor.ProcessAsync(Message(), CancellationToken.None));
    }

    [Fact]
    public async Task Marking_a_failed_delivery_rejects_a_pending_booking_once()
    {
        var store = Store();
        var processor = Processor(store, BookingStatuses.Confirmed);

        await processor.MarkFailedAsync("bkg_test", CancellationToken.None);
        await processor.MarkFailedAsync("bkg_test", CancellationToken.None);

        Assert.Equal(BookingStatuses.Rejected, store.Items["bkg_test"].Status);
        Assert.Equal(BookingStatuses.ProcessingFailedReason, store.Items["bkg_test"].RejectionReason);
        Assert.Equal(1, store.Completes);
    }

    private static InMemoryBookingStore Store(string status = BookingStatuses.Pending, string? code = null)
    {
        var store = new InMemoryBookingStore();
        store.Items["bkg_test"] = new Booking(
            "bkg_test",
            "h_porter",
            "The Porter",
            "r1",
            "Courtyard Single",
            "Anna Schmidt",
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 4),
            3,
            450m,
            status,
            code,
            status == BookingStatuses.Rejected ? "no" : null,
            Now,
            BookingTimeline.Initial(Now));
        return store;
    }

    private static BookingProcessor Processor(InMemoryBookingStore store, string outcome) =>
        new(store, new ScriptedOutcome(outcome), [0, 0, 0], () => Now);

    private static BookingMessage Message() => new("bkg_test", "h_porter", "r1", Now);

    private sealed class ScriptedOutcome(string status) : IBookingOutcomePolicy
    {
        public string Decide() => status;
    }
}

internal sealed class InMemoryBookingStore : IBookingStore
{
    public Dictionary<string, Booking> Items { get; } = new(StringComparer.Ordinal);
    public int EventWrites { get; private set; }
    public int Completes { get; private set; }
    public bool FailNextComplete { get; set; }

    public Task<Booking?> FindAsync(string id, CancellationToken ct) =>
        Task.FromResult(Items.TryGetValue(id, out var booking) ? booking : null);

    public Task UpdateEventsAsync(string id, IReadOnlyList<BookingEvent> events, CancellationToken ct)
    {
        EventWrites++;
        Items[id] = Items[id] with { Events = events };
        return Task.CompletedTask;
    }

    public Task<bool> TryCompleteAsync(string id, BookingCompletion completion, CancellationToken ct)
    {
        Completes++;
        if (FailNextComplete)
            return Task.FromResult(false);

        var booking = Items[id];
        if (booking.Status != BookingStatuses.Pending)
            return Task.FromResult(false);

        Items[id] = booking with
        {
            Status = completion.Status,
            ConfirmationCode = completion.ConfirmationCode,
            RejectionReason = completion.RejectionReason,
            Events = completion.Events,
        };
        return Task.FromResult(true);
    }
}

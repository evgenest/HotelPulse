namespace HotelPulse.Domain;

public sealed class BookingProcessor(
    IBookingStore store,
    IBookingOutcomePolicy outcomes,
    int[] stepDelayMs,
    Func<DateTime> utcNow)
{
    public async Task<ProcessingResult> ProcessAsync(BookingMessage message, CancellationToken ct)
    {
        var booking = await store.FindAsync(message.BookingId, ct);
        if (booking is null)
            throw new PermanentProcessingException($"Booking {message.BookingId} was not found.");

        if (BookingStatuses.IsTerminal(booking.Status))
            return new ProcessingResult.Skipped(booking.Status, booking.HotelId);

        await Task.Delay(stepDelayMs[0], ct);
        var delivered = BookingTimeline.Advance(booking.Events, BookingTimeline.Delivered, utcNow());
        await store.UpdateEventsAsync(booking.Id, delivered, ct);

        await Task.Delay(stepDelayMs[1], ct);
        var locked = BookingTimeline.Advance(delivered, BookingTimeline.Locked, utcNow());
        await store.UpdateEventsAsync(booking.Id, locked, ct);

        await Task.Delay(stepDelayMs[2], ct);

        var outcome = outcomes.Decide();
        var completedAt = utcNow();
        var completion = outcome == BookingStatuses.Confirmed
            ? new BookingCompletion(
                BookingStatuses.Confirmed,
                ConfirmationCodes.Create(),
                null,
                BookingTimeline.Terminal(booking.CreatedAt, completedAt, BookingStatuses.Confirmed))
            : new BookingCompletion(
                BookingStatuses.Rejected,
                null,
                BookingStatuses.UnavailableReason,
                BookingTimeline.Terminal(booking.CreatedAt, completedAt, BookingStatuses.Rejected));

        if (!BookingStatuses.IsLegalTransition(BookingStatuses.Pending, completion.Status))
            throw new PermanentProcessingException($"Refusing transition pending → {completion.Status}.");

        var wrote = await store.TryCompleteAsync(booking.Id, completion, ct);
        if (!wrote)
            return new ProcessingResult.Skipped(BookingStatuses.Pending, booking.HotelId);

        return new ProcessingResult.Completed(outcome, booking.HotelId);
    }

    public async Task MarkFailedAsync(string bookingId, CancellationToken ct)
    {
        var booking = await store.FindAsync(bookingId, ct);
        if (booking is null || BookingStatuses.IsTerminal(booking.Status))
            return;

        var completion = new BookingCompletion(
            BookingStatuses.Rejected,
            null,
            BookingStatuses.ProcessingFailedReason,
            BookingTimeline.Terminal(booking.CreatedAt, utcNow(), BookingStatuses.Rejected));

        await store.TryCompleteAsync(booking.Id, completion, ct);
    }
}

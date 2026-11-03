<?php

declare(strict_types=1);

namespace App\Domain;

use Closure;

/**
 * Moves a pending booking to confirmed or rejected. Same steps as
 * apps/HotelPulse.Domain/BookingProcessor.cs. Safe to run twice for the same
 * message: a finished booking is skipped, and the final write is a
 * compare-and-set from pending.
 */
final readonly class BookingProcessor
{
    /**
     * @param  array{int, int, int}  $stepDelayMs
     * @param  Closure(int): void  $sleepMs
     * @param  Closure(): \DateTimeImmutable  $now
     */
    public function __construct(
        private BookingStore $store,
        private BookingOutcomePolicy $outcomes,
        private array $stepDelayMs,
        private Closure $sleepMs,
        private Closure $now,
    ) {}

    public function process(BookingMessage $message): ProcessingResult
    {
        $booking = $this->store->find($message->bookingId)
            ?? throw new PermanentProcessingException("Booking {$message->bookingId} was not found.");

        if (BookingStatus::isTerminal($booking->status)) {
            return ProcessingResult::skipped($booking->status, $booking->hotelId);
        }

        ($this->sleepMs)($this->stepDelayMs[0]);
        $delivered = BookingTimeline::advance($booking->events, BookingTimeline::Delivered, ($this->now)());
        $this->store->updateEvents($booking->id, $delivered);

        ($this->sleepMs)($this->stepDelayMs[1]);
        $locked = BookingTimeline::advance($delivered, BookingTimeline::Locked, ($this->now)());
        $this->store->updateEvents($booking->id, $locked);

        ($this->sleepMs)($this->stepDelayMs[2]);

        $outcome = $this->outcomes->decide();
        $completedAt = ($this->now)();
        $completion = $outcome === BookingStatus::Confirmed
            ? new BookingCompletion(
                BookingStatus::Confirmed,
                ConfirmationCodes::create(),
                null,
                BookingTimeline::terminal($booking->createdAt, $completedAt, BookingStatus::Confirmed),
            )
            : new BookingCompletion(
                BookingStatus::Rejected,
                null,
                BookingStatus::UnavailableReason,
                BookingTimeline::terminal($booking->createdAt, $completedAt, BookingStatus::Rejected),
            );

        if (! BookingStatus::isLegalTransition(BookingStatus::Pending, $completion->status)) {
            throw new PermanentProcessingException("Refusing transition pending → {$completion->status}.");
        }

        if (! $this->store->tryComplete($booking->id, $completion)) {
            return ProcessingResult::skipped(BookingStatus::Pending, $booking->hotelId);
        }

        return ProcessingResult::completed($outcome, $booking->hotelId);
    }

    public function markFailed(string $bookingId): void
    {
        $booking = $this->store->find($bookingId);
        if ($booking === null || BookingStatus::isTerminal($booking->status)) {
            return;
        }

        $this->store->tryComplete($booking->id, new BookingCompletion(
            BookingStatus::Rejected,
            null,
            BookingStatus::ProcessingFailedReason,
            BookingTimeline::terminal($booking->createdAt, ($this->now)(), BookingStatus::Rejected),
        ));
    }
}

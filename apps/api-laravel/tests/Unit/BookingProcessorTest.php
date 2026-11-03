<?php

declare(strict_types=1);

// Port of tests/HotelPulse.UnitTests/BookingProcessorTests.cs.

use App\Domain\Booking;
use App\Domain\BookingCompletion;
use App\Domain\BookingMessage;
use App\Domain\BookingOutcomePolicy;
use App\Domain\BookingProcessor;
use App\Domain\BookingStatus;
use App\Domain\BookingStore;
use App\Domain\BookingTimeline;
use App\Domain\ConfirmationCodes;
use App\Domain\LocalDate;
use App\Domain\PermanentProcessingException;

/** In-memory store with the same compare-and-set rule as the SQL. */
final class InMemoryBookingStore implements BookingStore
{
    /** @var array<string, Booking> */
    public array $rows = [];

    public int $completions = 0;

    /** Simulates another writer finishing the booking first. */
    public bool $loseNextCompareAndSet = false;

    public function find(string $id): ?Booking
    {
        return $this->rows[$id] ?? null;
    }

    public function updateEvents(string $id, array $events): void
    {
        $row = $this->rows[$id];
        if ($row->status === BookingStatus::Pending) {
            $this->rows[$id] = clone_with($row, events: $events);
        }
    }

    public function tryComplete(string $id, BookingCompletion $completion): bool
    {
        if ($this->loseNextCompareAndSet || $this->rows[$id]->status !== BookingStatus::Pending) {
            $this->loseNextCompareAndSet = false;

            return false;
        }
        $this->completions++;
        $this->rows[$id] = clone_with(
            $this->rows[$id],
            status: $completion->status,
            confirmationCode: $completion->confirmationCode,
            rejectionReason: $completion->rejectionReason,
            events: $completion->events,
        );

        return true;
    }
}

function clone_with(Booking $b, ?string $status = null, ?string $confirmationCode = null, ?string $rejectionReason = null, ?array $events = null): Booking
{
    return new Booking(
        $b->id, $b->hotelId, $b->hotelName, $b->roomId, $b->roomType, $b->guestName,
        $b->checkIn, $b->checkOut, $b->nights, $b->total,
        $status ?? $b->status, $confirmationCode ?? $b->confirmationCode, $rejectionReason ?? $b->rejectionReason,
        $b->createdAt, $events ?? $b->events, $b->cancelledAt,
    );
}

function pendingBooking(string $status = BookingStatus::Pending): Booking
{
    $now = new DateTimeImmutable('2026-07-01T10:00:00Z');

    return new Booking('bkg_1', 'h_porter', 'Porter House', 'r1', 'Standard', 'Anna Schmidt',
        LocalDate::parse('2026-08-01'), LocalDate::parse('2026-08-03'), 2, '300.00',
        $status, null, null, $now, BookingTimeline::initial($now));
}

function processor(InMemoryBookingStore $store, string $outcome = BookingStatus::Confirmed): BookingProcessor
{
    $policy = new class($outcome) implements BookingOutcomePolicy
    {
        public function __construct(private string $outcome) {}

        public function decide(): string
        {
            return $this->outcome;
        }
    };

    return new BookingProcessor($store, $policy, [0, 0, 0], static fn (int $ms) => null,
        static fn () => new DateTimeImmutable('2026-07-01T10:00:03Z'));
}

function message(): BookingMessage
{
    return new BookingMessage('bkg_1', 'h_porter', 'r1', new DateTimeImmutable('2026-07-01T10:00:00Z'));
}

it('confirms a pending booking and gives it a code', function () {
    $store = new InMemoryBookingStore;
    $store->rows['bkg_1'] = pendingBooking();

    $result = processor($store)->process(message());

    $stored = $store->rows['bkg_1'];
    expect($result->changed)->toBeTrue()
        ->and($result->status)->toBe('confirmed')
        ->and($stored->status)->toBe('confirmed')
        ->and(ConfirmationCodes::isWellFormed($stored->confirmationCode))->toBeTrue()
        ->and($stored->events)->toHaveCount(5);
});

it('stores the reason and no code for a rejected outcome', function () {
    $store = new InMemoryBookingStore;
    $store->rows['bkg_1'] = pendingBooking();

    processor($store, BookingStatus::Rejected)->process(message());

    expect($store->rows['bkg_1']->status)->toBe('rejected')
        ->and($store->rows['bkg_1']->rejectionReason)->toBe(BookingStatus::UnavailableReason)
        ->and($store->rows['bkg_1']->confirmationCode)->toBeNull();
});

it('does not write an already confirmed booking again', function () {
    $store = new InMemoryBookingStore;
    $store->rows['bkg_1'] = pendingBooking(BookingStatus::Confirmed);

    $result = processor($store)->process(message());

    expect($result->changed)->toBeFalse()->and($store->completions)->toBe(0);
});

it('leaves alone a booking cancelled before delivery', function () {
    $store = new InMemoryBookingStore;
    $store->rows['bkg_1'] = pendingBooking(BookingStatus::Cancelled);

    $result = processor($store)->process(message());

    expect($result->changed)->toBeFalse()
        ->and($result->status)->toBe('cancelled')
        ->and($store->rows['bkg_1']->status)->toBe('cancelled');
});

it('treats a lost compare-and-set as already handled', function () {
    $store = new InMemoryBookingStore;
    $store->rows['bkg_1'] = pendingBooking();
    $store->loseNextCompareAndSet = true;

    $result = processor($store)->process(message());

    expect($result->changed)->toBeFalse()->and($result->status)->toBe('pending');
});

it('fails permanently for a missing booking', fn () => processor(new InMemoryBookingStore)->process(message()))
    ->throws(PermanentProcessingException::class);

it('rejects a pending booking once when a delivery is marked failed', function () {
    $store = new InMemoryBookingStore;
    $store->rows['bkg_1'] = pendingBooking();
    $processor = processor($store);

    $processor->markFailed('bkg_1');
    $processor->markFailed('bkg_1');

    expect($store->rows['bkg_1']->status)->toBe('rejected')
        ->and($store->rows['bkg_1']->rejectionReason)->toBe(BookingStatus::ProcessingFailedReason)
        ->and($store->completions)->toBe(1);
});

it('reads the queue message JSON from the contract', function () {
    $m = BookingMessage::fromJson('{"bookingId":"bkg_a","hotelId":"h_porter","roomId":"r2","createdAt":"2026-11-02T14:32:00Z"}');
    expect($m->bookingId)->toBe('bkg_a')->and(json_encode($m))
        ->toBe('{"bookingId":"bkg_a","hotelId":"h_porter","roomId":"r2","createdAt":"2026-11-02T14:32:00Z"}');
});

it('treats a broken queue message as permanent', fn (string $json) => BookingMessage::fromJson($json))
    ->with(['not json', '{}', '{"bookingId":""}', '[]'])
    ->throws(PermanentProcessingException::class);

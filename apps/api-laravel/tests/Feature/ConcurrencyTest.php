<?php

declare(strict_types=1);

// Requests that really run at the same time (separate processes). Port of
// tests/HotelPulse.IntegrationTests/ConcurrencyTests.cs; see ADR-0003…0005.

use App\Domain\BookingMessage;
use App\Domain\BookingProcessor;
use App\Domain\BookingTimeline;
use App\Domain\LocalDate;
use App\Infrastructure\Postgres\BookingRepository;
use Illuminate\Support\Facades\DB;

const PARALLEL = 10;

it('accepts exactly one of ten parallel bookings of the same room and dates', function () {
    $body = bookingBody(100);

    $codes = inParallel(PARALLEL, fn (int $i) => $this->postJson('/api/bookings', ['guestName' => "Guest {$i}"] + $body)->status());

    expect(array_count_values($codes))->toEqual(['202' => 1, '409' => PARALLEL - 1])
        ->and(DB::table('bookings')->count())->toBe(1)
        ->and(DB::table('outbox')->count())->toBe(1);
});

it('creates one booking for ten parallel requests with one idempotency key', function () {
    $body = bookingBody(110);

    $results = inParallel(PARALLEL, fn () => ($r = $this->postJson('/api/bookings', $body, ['Idempotency-Key' => 'parallel-1']))->status().' '.$r->json('id'));

    expect(array_unique($results))->toHaveCount(1)
        ->and($results[0])->toStartWith('202 bkg_')
        ->and(DB::table('bookings')->count())->toBe(1);
});

it('keeps worker steps written after the api read the row when cancelling', function () {
    $id = $this->postJson('/api/bookings', bookingBody(120))->json('id');
    $repository = new BookingRepository;
    $readByApi = $repository->find($id);

    $now = new DateTimeImmutable;
    $delivered = BookingTimeline::advance($readByApi->events, BookingTimeline::Delivered, $now);
    $repository->updateEvents($id, $delivered);

    $cancelled = $repository->tryCancel($id, LocalDate::todayUtc(), BookingTimeline::cancelledEvent($now));

    expect($cancelled->status)->toBe('cancelled')
        ->and($cancelled->events)->toHaveCount(count($delivered) + 1)
        ->and($cancelled->events[BookingTimeline::Delivered]->done)->toBeTrue()
        ->and($cancelled->events[count($cancelled->events) - 1]->label)->toBe('api → booking cancelled');
});

it('always ends cancelled with a free room when a cancel races the worker', function () {
    for ($round = 0; $round < 5; $round++) {
        $body = bookingBody(130 + $round * 10);
        $id = $this->postJson('/api/bookings', $body)->json('id');

        $results = inParallel(2, fn (int $i) => $i === 0
            ? $this->postJson("/api/bookings/{$id}/cancel")->status()
            : (app(BookingProcessor::class)->process(new BookingMessage($id, 'h_porter', 'r1', new DateTimeImmutable))->changed ? 'worker-wrote' : 'worker-skipped'));

        expect($results[0])->toBe('200');
        $booking = $this->getJson("/api/bookings/{$id}")->json();
        expect($booking['status'])->toBe('cancelled')
            ->and(end($booking['events'])['label'])->toBe('api → booking cancelled');

        $rooms = collect($this->getJson("/api/hotels/h_porter/availability?checkIn={$body['checkIn']}&checkOut={$body['checkOut']}")->json('rooms'))->keyBy('id');
        expect($rooms['r1']['available'])->toBeTrue();
    }
});

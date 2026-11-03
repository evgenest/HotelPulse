<?php

declare(strict_types=1);

use App\Domain\BookingCancellation;
use App\Domain\BookingEvent;
use App\Domain\BookingLabels;
use App\Domain\BookingStatus;
use App\Domain\BookingTimeline;
use App\Domain\CancellationDecision;
use App\Domain\LocalDate;

$at = new DateTimeImmutable('2026-07-01T14:32:00Z');

it('starts a new booking as received and waiting to publish', function () use ($at) {
    $events = BookingTimeline::initial($at);

    expect($events)->toHaveCount(4)
        ->and($events[0]->done)->toBeTrue()
        ->and($events[0]->current)->toBeFalse()
        ->and($events[0]->time)->toBe('14:32:00')
        ->and($events[1]->done)->toBeFalse()
        ->and($events[1]->current)->toBeTrue()
        ->and($events[3]->label)->toBe(BookingLabels::Locked);
});

it('marks the publish step done when advancing to delivered', function () use ($at) {
    $events = BookingTimeline::advance(BookingTimeline::initial($at), BookingTimeline::Delivered, $at->modify('+1 second'));

    expect(array_map(fn (BookingEvent $e) => $e->done, array_slice($events, 0, 3)))->toBe([true, true, true])
        ->and($events[3]->current)->toBeTrue()
        ->and($events[3]->done)->toBeFalse()
        ->and($events[1]->time)->toBe('14:32:01');
});

it('leaves no current step after advancing to locked', function () use ($at) {
    $delivered = BookingTimeline::advance(BookingTimeline::initial($at), BookingTimeline::Delivered, $at);
    $locked = BookingTimeline::advance($delivered, BookingTimeline::Locked, $at->modify('+1 second'));

    foreach ($locked as $step) {
        expect($step->done)->toBeTrue()->and($step->current)->toBeFalse();
    }
});

it('keeps completed steps and their times on a second advance', function () use ($at) {
    $once = BookingTimeline::advance(BookingTimeline::initial($at), BookingTimeline::Delivered, $at);
    $twice = BookingTimeline::advance($once, BookingTimeline::Delivered, $at->modify('+1 minute'));

    expect(array_map(fn ($e) => $e->done, $twice))->toBe(array_map(fn ($e) => $e->done, $once))
        ->and($twice[0]->time)->toBe($once[0]->time);
});

it('records the outcome in the terminal timeline', function () use ($at) {
    $events = BookingTimeline::terminal($at, $at->modify('+3 seconds'), BookingStatus::Confirmed);

    expect($events)->toHaveCount(5)
        ->and(end($events)->label)->toBe(BookingLabels::status('confirmed'))
        ->and($events[2]->time)->toBe('14:32:00');
});

it('serialises an event like the .NET API, without a null time', function () {
    expect(json_encode(new BookingEvent('x', false, null, true)))->toBe('{"label":"x","done":false,"current":true}');
});

describe('cancellation', function () use ($at) {
    $today = LocalDate::parse('2026-07-01');

    it('follows the spec', function (string $status, int $checkInInDays, CancellationDecision $expected) use ($today) {
        expect(BookingCancellation::decide($status, $today->addDays($checkInInDays), $today))->toBe($expected);
    })->with([
        ['pending', 1, CancellationDecision::Cancel],
        ['confirmed', 1, CancellationDecision::Cancel],
        ['confirmed', 30, CancellationDecision::Cancel],
        ['pending', 0, CancellationDecision::TooLate],
        ['confirmed', 0, CancellationDecision::TooLate],
        ['confirmed', -2, CancellationDecision::TooLate],
        ['rejected', 5, CancellationDecision::NotCancellable],
        ['cancelled', 5, CancellationDecision::AlreadyCancelled],
        ['cancelled', -5, CancellationDecision::AlreadyCancelled],
    ]);

    it('treats an unknown status as not cancellable', fn () => expect(BookingCancellation::decide('mystery', $today->addDays(5), $today))->toBe(CancellationDecision::NotCancellable));

    it('gives an error only for refusals', function (CancellationDecision $decision, ?string $error) {
        expect(BookingCancellation::error($decision))->toBe($error);
    })->with([
        [CancellationDecision::NotCancellable, BookingCancellation::RejectedError],
        [CancellationDecision::TooLate, BookingCancellation::TooLateError],
        [CancellationDecision::Cancel, null],
        [CancellationDecision::AlreadyCancelled, null],
    ]);

    it('appends one event and clears the current marker', function () use ($at) {
        $events = BookingTimeline::initial($at);
        $cancelled = BookingTimeline::cancelled($events, $at->modify('+1 minute'));

        expect($cancelled)->toHaveCount(count($events) + 1)
            ->and(end($cancelled)->label)->toBe(BookingLabels::Cancelled)
            ->and(array_filter($cancelled, fn ($e) => $e->current))->toBe([]);
    });
});

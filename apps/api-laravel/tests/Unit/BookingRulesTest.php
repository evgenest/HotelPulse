<?php

declare(strict_types=1);

// Port of tests/HotelPulse.UnitTests/BookingLogicTests.cs. Same cases, same
// expectations: both implementations must follow one set of rules.

use App\Domain\BookingOutcome;
use App\Domain\BookingStatus;
use App\Domain\CacheKeys;
use App\Domain\ConfirmationCodes;
use App\Domain\ConfirmProbability;
use App\Domain\DeliveryPolicy;
use App\Domain\FailureKind;
use App\Domain\LocalDate;
use App\Domain\QueueAction;
use App\Domain\StayOverlap;
use App\Domain\StepDelays;

describe('booking status', function () {
    it('only moves pending to a terminal status', function (string $from, string $to, bool $legal) {
        expect(BookingStatus::isLegalTransition($from, $to))->toBe($legal);
    })->with([
        ['pending', 'confirmed', true],
        ['pending', 'rejected', true],
        ['confirmed', 'confirmed', false],
        ['confirmed', 'rejected', false],
        ['rejected', 'confirmed', false],
        ['rejected', 'pending', false],
        ['pending', 'cancelled', false],
        ['cancelled', 'confirmed', false],
    ]);

    it('treats confirmed, rejected and cancelled as terminal', function (string $status, bool $terminal) {
        expect(BookingStatus::isTerminal($status))->toBe($terminal);
    })->with([['pending', false], ['confirmed', true], ['rejected', true], ['cancelled', true]]);

    it('lets only pending and confirmed hold the room', function (string $status, bool $holds) {
        expect(BookingStatus::holdsInventory($status))->toBe($holds);
    })->with([['pending', true], ['confirmed', true], ['rejected', false], ['cancelled', false]]);
});

describe('stay overlap', function () {
    $d = fn (int $day) => LocalDate::parse('2026-07-01')->addDays($day);

    it('collides when ranges overlap', fn () => expect(StayOverlap::overlaps($d(0), $d(4), $d(3), $d(6)))->toBeTrue());
    it('keeps the checkout morning free for the next guest', fn () => expect(StayOverlap::overlaps($d(0), $d(4), $d(4), $d(6)))->toBeFalse());
    it('does not collide when one stay is entirely before another', fn () => expect(StayOverlap::overlaps($d(0), $d(2), $d(3), $d(5)))->toBeFalse());
    it('treats containment as an overlap', fn () => expect(StayOverlap::overlaps($d(0), $d(10), $d(2), $d(3)))->toBeTrue());
});

describe('booking outcome', function () {
    it('confirms when the roll is below the probability', function (float $roll, float $probability, string $status) {
        expect(BookingOutcome::fromRoll($roll, $probability))->toBe($status);
    })->with([
        [0.0, 0.9, 'confirmed'],
        [0.899, 0.9, 'confirmed'],
        [0.9, 0.9, 'rejected'],
        [0.999, 1.0, 'confirmed'],
        [0.0, 0.0, 'rejected'],
    ]);

    it('rejects a roll outside the unit interval', fn () => BookingOutcome::fromRoll(1.0, 0.5))
        ->throws(InvalidArgumentException::class);
});

describe('delivery policy', function () {
    it('retries once unless the database is down', function (FailureKind $kind, bool $redelivered, QueueAction $action, bool $markRejected) {
        $plan = DeliveryPolicy::decide($kind, $redelivered);
        expect($plan->action)->toBe($action)
            ->and($plan->markRejected)->toBe($markRejected);
    })->with([
        [FailureKind::Permanent, false, QueueAction::Discard, true],
        [FailureKind::Permanent, true, QueueAction::Discard, true],
        [FailureKind::Transient, false, QueueAction::Requeue, false],
        [FailureKind::Transient, true, QueueAction::Discard, true],
        [FailureKind::DatabaseUnavailable, false, QueueAction::Requeue, false],
        [FailureKind::DatabaseUnavailable, true, QueueAction::Requeue, false],
    ]);
});

describe('confirmation codes', function () {
    it('uses HP- and six upper-case hex characters', function () {
        $code = ConfirmationCodes::create("\xa3\xf9\xc1");
        expect($code)->toBe('HP-A3F9C1')
            ->and(ConfirmationCodes::isWellFormed($code))->toBeTrue()
            ->and(ConfirmationCodes::isWellFormed(ConfirmationCodes::create()))->toBeTrue();
    });

    it('rejects malformed codes', fn (?string $code) => expect(ConfirmationCodes::isWellFormed($code))->toBeFalse())
        ->with([null, '', 'HP-a3f9c1', 'HP-A3F9', 'XX-A3F9C1']);
});

describe('worker settings', function () {
    it('keeps the original pacing by default', fn () => expect(StepDelays::resolve(null))->toBe([700, 800, 900]));

    it('replaces every step with an explicit delay', function (string $value) {
        expect(StepDelays::resolve($value))->toBe([(int) $value, (int) $value, (int) $value]);
    })->with(['0', '15']);

    it('falls back to the product pacing for invalid overrides', fn (string $value) => expect(StepDelays::resolve($value))->toBe(StepDelays::Default))
        ->with(['', 'fast', '-1']);

    it('defaults the confirm probability to 0.9 and clamps it', function (?string $value, float $expected) {
        expect(ConfirmProbability::resolve($value))->toBe($expected);
    })->with([[null, 0.9], ['', 0.9], ['abc', 0.9], ['1', 1.0], ['0.25', 0.25], ['2', 1.0], ['-1', 0.0]]);
});

describe('cache keys', function () {
    it('changes the key on a version bump so the previous entry is unused', function () {
        expect(CacheKeys::hotelList(1))->not->toBe(CacheKeys::hotelList(2))
            ->and(CacheKeys::availability('h_porter', 4, LocalDate::parse('2026-07-01'), LocalDate::parse('2026-07-04')))
            ->toBe('hp:avail:h_porter:v4:2026-07-01:2026-07-04');
    });
});

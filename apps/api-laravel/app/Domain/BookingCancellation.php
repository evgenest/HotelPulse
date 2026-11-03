<?php

declare(strict_types=1);

namespace App\Domain;

/**
 * The rules from docs/specs/booking-cancellation.md. The SQL update repeats
 * them in its WHERE clause, so a concurrent change between the read and the
 * write cannot slip through.
 */
final class BookingCancellation
{
    public const string RejectedError = 'A rejected booking cannot be cancelled.';

    public const string TooLateError = 'Bookings can only be cancelled before the check-in date.';

    public static function decide(string $status, LocalDate $checkIn, LocalDate $today): CancellationDecision
    {
        return match (true) {
            $status === BookingStatus::Cancelled => CancellationDecision::AlreadyCancelled,
            $status === BookingStatus::Rejected => CancellationDecision::NotCancellable,
            BookingStatus::holdsInventory($status) && ! $checkIn->isAfter($today) => CancellationDecision::TooLate,
            BookingStatus::holdsInventory($status) => CancellationDecision::Cancel,
            default => CancellationDecision::NotCancellable,
        };
    }

    public static function error(CancellationDecision $decision): ?string
    {
        return match ($decision) {
            CancellationDecision::NotCancellable => self::RejectedError,
            CancellationDecision::TooLate => self::TooLateError,
            default => null,
        };
    }
}

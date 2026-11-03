<?php

declare(strict_types=1);

namespace App\Domain;

/**
 * Booking statuses and the transitions between them. Statuses are stored as
 * text in Postgres, so they stay strings here (see 001_init.sql).
 */
final class BookingStatus
{
    public const string Pending = 'pending';

    public const string Confirmed = 'confirmed';

    public const string Rejected = 'rejected';

    public const string Cancelled = 'cancelled';

    public const string UnavailableReason = 'Room became unavailable during processing.';

    public const string ProcessingFailedReason = 'Processing failed after a retry. The stay was not confirmed.';

    /** Terminal for the worker. A confirmed booking can still be cancelled by the guest. */
    public static function isTerminal(string $status): bool
    {
        return in_array($status, [self::Confirmed, self::Rejected, self::Cancelled], true);
    }

    /** Only these statuses hold the room; the exclusion constraint filters on the same set. */
    public static function holdsInventory(string $status): bool
    {
        return in_array($status, [self::Pending, self::Confirmed], true);
    }

    /** The only forward moves are pending → confirmed and pending → rejected. */
    public static function isLegalTransition(string $from, string $to): bool
    {
        return $from === self::Pending && in_array($to, [self::Confirmed, self::Rejected], true);
    }
}

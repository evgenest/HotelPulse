<?php

declare(strict_types=1);

namespace App\Domain;

/**
 * Half-open stay overlap: a checkout morning does not collide with the next
 * check-in. The exclusion constraint uses the same predicate.
 */
final class StayOverlap
{
    public static function overlaps(LocalDate $checkIn, LocalDate $checkOut, LocalDate $otherCheckIn, LocalDate $otherCheckOut): bool
    {
        return $checkIn->isBefore($otherCheckOut) && $checkOut->isAfter($otherCheckIn);
    }
}

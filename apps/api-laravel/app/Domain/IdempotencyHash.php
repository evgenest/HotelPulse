<?php

declare(strict_types=1);

namespace App\Domain;

/**
 * Fingerprint of a create request, stored next to its Idempotency-Key. A
 * repeat with the same key and a different body is a conflict.
 */
final class IdempotencyHash
{
    public static function compute(CreateBookingCommand $command): string
    {
        $canonical = implode("\n", [
            trim($command->hotelId),
            trim($command->roomId),
            trim($command->guestName ?? ''),
            trim($command->checkIn ?? ''),
            trim($command->checkOut ?? ''),
            (string) $command->nights,
            (string) $command->total,
        ]);

        return strtoupper(hash('sha256', $canonical));
    }
}

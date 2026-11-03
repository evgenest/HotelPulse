<?php

declare(strict_types=1);

namespace App\Domain;

final readonly class CreateBookingCommand
{
    /** @param numeric-string|null $total */
    public function __construct(
        public string $hotelId,
        public string $roomId,
        public ?string $guestName,
        public ?string $checkIn,
        public ?string $checkOut,
        public ?int $nights,
        public ?string $total,
    ) {}
}

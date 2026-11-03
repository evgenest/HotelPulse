<?php

declare(strict_types=1);

namespace App\Domain;

final readonly class BookingCompletion
{
    /** @param list<BookingEvent> $events */
    public function __construct(
        public string $status,
        public ?string $confirmationCode,
        public ?string $rejectionReason,
        public array $events,
    ) {}
}

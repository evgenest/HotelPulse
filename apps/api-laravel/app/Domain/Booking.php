<?php

declare(strict_types=1);

namespace App\Domain;

use DateTimeImmutable;
use JsonSerializable;

final readonly class Booking implements JsonSerializable
{
    /**
     * @param  numeric-string  $total
     * @param  list<BookingEvent>  $events
     */
    public function __construct(
        public string $id,
        public string $hotelId,
        public string $hotelName,
        public string $roomId,
        public string $roomType,
        public string $guestName,
        public LocalDate $checkIn,
        public LocalDate $checkOut,
        public int $nights,
        public string $total,
        public string $status,
        public ?string $confirmationCode,
        public ?string $rejectionReason,
        public DateTimeImmutable $createdAt,
        public array $events,
        public ?DateTimeImmutable $cancelledAt = null,
    ) {}

    /** Same field names, order and null handling as the .NET API (nulls omitted). */
    public function jsonSerialize(): array
    {
        $json = [
            'id' => $this->id,
            'hotelId' => $this->hotelId,
            'hotelName' => $this->hotelName,
            'roomId' => $this->roomId,
            'roomType' => $this->roomType,
            'guestName' => $this->guestName,
            'checkIn' => (string) $this->checkIn,
            'checkOut' => (string) $this->checkOut,
            'nights' => $this->nights,
            'total' => Money::toJsonNumber($this->total),
            'status' => $this->status,
            'confirmationCode' => $this->confirmationCode,
            'rejectionReason' => $this->rejectionReason,
            'createdAt' => Clock::toJson($this->createdAt),
            'events' => $this->events,
            'cancelledAt' => $this->cancelledAt === null ? null : Clock::toJson($this->cancelledAt),
        ];

        return array_filter($json, static fn (mixed $value): bool => $value !== null);
    }
}

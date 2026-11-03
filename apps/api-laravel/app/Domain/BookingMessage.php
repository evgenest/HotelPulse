<?php

declare(strict_types=1);

namespace App\Domain;

use DateTimeImmutable;
use JsonSerializable;

/** The queue message. Same JSON as docs/api.md → Queue. */
final readonly class BookingMessage implements JsonSerializable
{
    public function __construct(
        public string $bookingId,
        public string $hotelId,
        public string $roomId,
        public DateTimeImmutable $createdAt,
    ) {}

    public static function fromJson(string $json): self
    {
        try {
            $data = json_decode($json, true, 8, JSON_THROW_ON_ERROR);
        } catch (\JsonException $e) {
            throw new PermanentProcessingException('Booking message was not valid JSON.');
        }
        if (! is_array($data) || ! is_string($data['bookingId'] ?? null) || $data['bookingId'] === '') {
            throw new PermanentProcessingException('Booking message was empty.');
        }

        try {
            $createdAt = new DateTimeImmutable(is_string($data['createdAt'] ?? null) ? $data['createdAt'] : 'now');
        } catch (\Exception) {
            throw new PermanentProcessingException('Booking message had an invalid createdAt.');
        }

        return new self(
            $data['bookingId'],
            is_string($data['hotelId'] ?? null) ? $data['hotelId'] : '',
            is_string($data['roomId'] ?? null) ? $data['roomId'] : '',
            $createdAt,
        );
    }

    /** @return array<string, mixed> */
    public function jsonSerialize(): array
    {
        return [
            'bookingId' => $this->bookingId,
            'hotelId' => $this->hotelId,
            'roomId' => $this->roomId,
            'createdAt' => Clock::toJson($this->createdAt),
        ];
    }
}

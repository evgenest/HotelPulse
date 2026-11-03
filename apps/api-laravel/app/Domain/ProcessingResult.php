<?php

declare(strict_types=1);

namespace App\Domain;

final readonly class ProcessingResult
{
    private function __construct(
        public string $status,
        public string $hotelId,
        public bool $changed,
    ) {}

    public static function completed(string $status, string $hotelId): self
    {
        return new self($status, $hotelId, true);
    }

    public static function skipped(string $status, string $hotelId): self
    {
        return new self($status, $hotelId, false);
    }
}

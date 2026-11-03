<?php

declare(strict_types=1);

namespace App\Domain;

final readonly class Room
{
    /** @param numeric-string $price */
    public function __construct(
        public string $id,
        public string $type,
        public int $capacity,
        public string $price,
        public int $sqm,
    ) {}
}

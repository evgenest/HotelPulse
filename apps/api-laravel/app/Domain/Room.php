<?php

declare(strict_types=1);

namespace App\Domain;

use JsonSerializable;

final readonly class Room implements JsonSerializable
{
    /** @param numeric-string $price */
    public function __construct(
        public string $id,
        public string $type,
        public int $capacity,
        public string $price,
        public int $sqm,
    ) {}

    /** @return array<string, mixed> */
    public function jsonSerialize(): array
    {
        return [
            'id' => $this->id,
            'type' => $this->type,
            'capacity' => $this->capacity,
            'price' => Money::toJsonNumber($this->price),
            'sqm' => $this->sqm,
        ];
    }
}

<?php

declare(strict_types=1);

namespace App\Domain;

final readonly class Hotel
{
    /**
     * @param  numeric-string  $priceFrom
     * @param  list<string>  $amenities
     * @param  list<Room>  $rooms
     */
    public function __construct(
        public string $id,
        public string $name,
        public string $city,
        public int $rating,
        public string $glyph,
        public string $priceFrom,
        public string $description,
        public array $amenities,
        public array $rooms,
    ) {}

    public function room(string $id): ?Room
    {
        foreach ($this->rooms as $room) {
            if ($room->id === $id) {
                return $room;
            }
        }

        return null;
    }
}

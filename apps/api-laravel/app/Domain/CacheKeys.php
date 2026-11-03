<?php

declare(strict_types=1);

namespace App\Domain;

/**
 * Versioned cache keys, same scheme as the .NET API. Invalidation bumps a
 * version number, so old entries are simply never read again.
 */
final class CacheKeys
{
    public const string CatalogVersion = 'hp:ver:catalog';

    public static function hotelVersion(string $hotelId): string
    {
        return "hp:ver:hotel:{$hotelId}";
    }

    public static function hotelList(int $version): string
    {
        return "hp:hotels:v{$version}";
    }

    public static function hotel(string $id, int $version): string
    {
        return "hp:hotels:{$id}:v{$version}";
    }

    public static function availability(string $hotelId, int $version, LocalDate $checkIn, LocalDate $checkOut): string
    {
        return "hp:avail:{$hotelId}:v{$version}:{$checkIn}:{$checkOut}";
    }
}

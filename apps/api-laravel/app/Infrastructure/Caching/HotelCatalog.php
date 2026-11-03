<?php

declare(strict_types=1);

namespace App\Infrastructure\Caching;

use App\Domain\CacheKeys;
use App\Domain\LocalDate;
use App\Infrastructure\Postgres\HotelRepository;

/**
 * Read-through cache for the catalog and availability. Values are stored and
 * returned in their JSON shape, so a HIT and a MISS give the same body.
 * Same decisions as HotelCatalog.cs.
 */
final readonly class HotelCatalog
{
    public function __construct(private HotelRepository $hotels, private HotelCache $cache) {}

    public function list(): CachedResult
    {
        $version = $this->cache->version(CacheKeys::CatalogVersion);
        if ($version === null) {
            return new CachedResult(self::shape($this->hotels->list()), 'BYPASS');
        }

        $key = CacheKeys::hotelList($version);
        [$ok, $cached] = $this->cache->get($key);
        if (! $ok) {
            return new CachedResult(self::shape($this->hotels->list()), 'BYPASS');
        }
        if ($cached !== null) {
            return new CachedResult($cached, 'HIT');
        }

        $rows = self::shape($this->hotels->list());
        $this->cache->set($key, $rows);

        return new CachedResult($rows, 'MISS');
    }

    public function get(string $id): ?CachedResult
    {
        $version = $this->cache->version(CacheKeys::hotelVersion($id));
        if ($version === null) {
            $direct = $this->hotels->get($id);

            return $direct === null ? null : new CachedResult(self::shape($direct), 'BYPASS');
        }

        $key = CacheKeys::hotel($id, $version);
        [$ok, $cached] = $this->cache->get($key);
        if ($ok && $cached !== null) {
            return new CachedResult($cached, 'HIT');
        }

        $hotel = $this->hotels->get($id);
        if ($hotel === null) {
            return null;
        }
        $body = self::shape($hotel);
        if ($ok) {
            $this->cache->set($key, $body);
        }

        return new CachedResult($body, $ok ? 'MISS' : 'BYPASS');
    }

    public function availability(string $hotelId, LocalDate $checkIn, LocalDate $checkOut): ?CachedResult
    {
        $version = $this->cache->version(CacheKeys::hotelVersion($hotelId));
        if ($version === null) {
            $direct = $this->hotels->availability($hotelId, $checkIn, $checkOut);
            if ($direct === [] && $this->hotels->get($hotelId) === null) {
                return null;
            }

            return new CachedResult(self::shape($direct), 'BYPASS');
        }

        $key = CacheKeys::availability($hotelId, $version, $checkIn, $checkOut);
        [$ok, $cached] = $this->cache->get($key);
        if ($ok && $cached !== null) {
            return new CachedResult($cached, 'HIT');
        }

        if ($this->hotels->get($hotelId) === null) {
            return null;
        }
        $rows = self::shape($this->hotels->availability($hotelId, $checkIn, $checkOut));
        if ($ok) {
            $this->cache->set($key, $rows);
        }

        return new CachedResult($rows, $ok ? 'MISS' : 'BYPASS');
    }

    /** The JSON shape, as plain arrays, so cached and fresh bodies match. */
    private static function shape(mixed $value): mixed
    {
        return json_decode(json_encode($value, JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE), true, 16, JSON_THROW_ON_ERROR);
    }
}

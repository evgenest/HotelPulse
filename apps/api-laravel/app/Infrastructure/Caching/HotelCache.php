<?php

declare(strict_types=1);

namespace App\Infrastructure\Caching;

use App\Domain\CacheKeys;
use Illuminate\Redis\RedisManager;
use Psr\Log\LoggerInterface;
use Throwable;

/**
 * Redis is only a cache. Every call catches its own failure and reports it,
 * so a Redis outage turns into X-Cache: BYPASS and a Postgres read, never an
 * error for the client (root CLAUDE.md). Same behaviour as RedisHotelCache.cs.
 */
final readonly class HotelCache
{
    public const int TtlSeconds = 60;

    public function __construct(private RedisManager $redis, private LoggerInterface $log) {}

    /** Current version of a key family; null means Redis is unavailable. */
    public function version(string $key): ?int
    {
        try {
            $value = $this->redis->connection()->get($key);

            return $value === null ? 0 : (int) $value;
        } catch (Throwable $e) {
            $this->log->warning('Redis version read failed', ['key' => $key, 'error' => $e->getMessage()]);

            return null;
        }
    }

    /**
     * @return array{bool, mixed} [ok, decoded value or null on a miss]
     */
    public function get(string $key): array
    {
        try {
            $value = $this->redis->connection()->get($key);
            if ($value === null) {
                return [true, null];
            }

            return [true, json_decode((string) $value, true, 16, JSON_THROW_ON_ERROR)];
        } catch (Throwable $e) {
            $this->log->warning('Redis read failed', ['key' => $key, 'error' => $e->getMessage()]);

            return [false, null];
        }
    }

    public function set(string $key, mixed $value): void
    {
        try {
            $this->redis->connection()->setex($key, self::TtlSeconds, json_encode($value, JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE));
        } catch (Throwable $e) {
            $this->log->warning('Redis write failed', ['key' => $key, 'error' => $e->getMessage()]);
        }
    }

    /** Bumps the catalog and hotel versions, so every older entry is unused. */
    public function invalidateHotel(string $hotelId): void
    {
        try {
            $this->redis->connection()->pipeline(function ($pipe) use ($hotelId): void {
                $pipe->incr(CacheKeys::CatalogVersion);
                $pipe->incr(CacheKeys::hotelVersion($hotelId));
            });
        } catch (Throwable $e) {
            $this->log->warning('Cache invalidation failed', ['hotelId' => $hotelId, 'error' => $e->getMessage()]);
        }
    }
}

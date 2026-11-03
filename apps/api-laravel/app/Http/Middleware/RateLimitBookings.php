<?php

declare(strict_types=1);

namespace App\Http\Middleware;

use Closure;
use Illuminate\Http\Request;
use Illuminate\Redis\RedisManager;
use Illuminate\Support\Facades\Log;
use Symfony\Component\HttpFoundation\Response;
use Throwable;

/**
 * At most N booking requests per minute per client IP, counted in Redis so
 * every API copy shares the count. If Redis is down the request goes through:
 * a cache outage must never fail a request (root CLAUDE.md), and that rule
 * wins over the limit.
 */
final readonly class RateLimitBookings
{
    public function __construct(private RedisManager $redis) {}

    public function handle(Request $request, Closure $next): Response
    {
        $limit = (int) config('hotelpulse.booking_rate_limit_per_minute');
        if ($limit <= 0) {
            return $next($request);
        }

        $window = intdiv(time(), 60);
        $key = 'hp:ratelimit:bookings:'.sha1((string) $request->ip()).':'.$window;

        try {
            $connection = $this->redis->connection();
            $count = (int) $connection->incr($key);
            if ($count === 1) {
                $connection->expire($key, 120);
            }
        } catch (Throwable $e) {
            Log::warning('Rate limit skipped: Redis unavailable', ['error' => $e->getMessage()]);

            return $next($request);
        }

        if ($count > $limit) {
            return response()
                ->json(['error' => 'Too many booking requests. Try again in a minute.'], 429)
                ->header('Retry-After', (string) (60 - time() % 60));
        }

        return $next($request);
    }
}

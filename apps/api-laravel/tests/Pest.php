<?php

declare(strict_types=1);

use App\Domain\LocalDate;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Redis;
use Tests\TestCase;

// Feature tests boot the app against real Postgres, Redis and RabbitMQ
// (DATABASE_URL, REDIS_URL, AMQP_URL). Unit tests are plain PHP.
pest()->extend(TestCase::class)
    ->beforeEach(function () {
        static $migrated = false;
        if (! $migrated) {
            $this->artisan('hotelpulse:migrate')->assertSuccessful();
            $migrated = true;
        }
        DB::statement('TRUNCATE outbox, idempotency_keys, bookings RESTART IDENTITY');
        try {
            Redis::connection()->flushdb();
        } catch (Throwable) {
            // Tests that need Redis assert on X-Cache; the rest do not care.
        }
    })
    ->in('Feature');

/** A stay that starts $daysAhead days from today (UTC) and lasts $nights. */
function stay(int $daysAhead, int $nights = 3): array
{
    $checkIn = LocalDate::todayUtc()->addDays($daysAhead);

    return [(string) $checkIn, (string) $checkIn->addDays($nights)];
}

/** A valid POST /api/bookings body for h_porter r1 (150 per night). */
function bookingBody(int $daysAhead, array $overrides = [], int $nights = 3): array
{
    [$checkIn, $checkOut] = stay($daysAhead, $nights);

    return array_merge([
        'hotelId' => 'h_porter',
        'roomId' => 'r1',
        'guestName' => 'Anna Schmidt',
        'checkIn' => $checkIn,
        'checkOut' => $checkOut,
        'nights' => $nights,
        'total' => 150 * $nights,
    ], $overrides);
}

/**
 * Runs $work in $count separate processes at the same moment and returns what
 * each returned. Each child opens its own database and Redis connections, so
 * this is real concurrency against Postgres, not interleaving in one process.
 *
 * @param  Closure(int): (string|int)  $work
 * @return list<string>
 */
function inParallel(int $count, Closure $work): array
{
    $dir = sys_get_temp_dir().'/hp-par-'.bin2hex(random_bytes(4));
    mkdir($dir);
    $start = microtime(true) + 0.3;
    $pids = [];

    DB::disconnect();
    for ($i = 0; $i < $count; $i++) {
        $pid = pcntl_fork();
        if ($pid === 0) {
            DB::purge();
            Redis::purge();
            time_sleep_until($start);
            try {
                $result = (string) $work($i);
            } catch (Throwable $e) {
                $result = 'exception: '.$e::class.': '.$e->getMessage();
            }
            file_put_contents("{$dir}/{$i}", $result);
            // Leave without running PHPUnit's shutdown handlers in the child.
            posix_kill(posix_getpid(), SIGKILL);
        }
        $pids[] = $pid;
    }
    foreach ($pids as $pid) {
        pcntl_waitpid($pid, $status);
    }
    DB::reconnect();

    $results = [];
    for ($i = 0; $i < $count; $i++) {
        $results[] = (string) @file_get_contents("{$dir}/{$i}");
        @unlink("{$dir}/{$i}");
    }
    @rmdir($dir);

    return $results;
}

<?php

declare(strict_types=1);

// ADR-0011: migrations run once under a lock that cannot be left behind.

use Illuminate\Support\Facades\Artisan;
use Illuminate\Support\Facades\DB;

it('lets parallel migrators all finish and leaves no advisory lock', function () {
    $before = DB::table('migrations')->count();

    $codes = inParallel(3, fn () => Artisan::call('hotelpulse:migrate'));

    expect($codes)->toBe(['0', '0', '0'])
        ->and(DB::table('migrations')->count())->toBe($before)
        ->and(DB::selectOne("SELECT count(*) AS n FROM pg_locks WHERE locktype = 'advisory'")->n)->toBe(0);
});

it('waits for the lock and runs once it is released', function () {
    $results = inParallel(2, function (int $i) {
        if ($i === 0) {
            // Another migrator holds the lock for one second.
            $holder = DB::connection('pgsql_lock');
            $holder->beginTransaction();
            $holder->select('SELECT pg_advisory_xact_lock(48291002)');
            usleep(1_000_000);
            $holder->rollBack();

            return 'released';
        }

        usleep(200_000); // start after the lock is taken
        $started = microtime(true);
        $code = Artisan::call('hotelpulse:migrate');

        return $code.' '.round(microtime(true) - $started, 2);
    });

    [$code, $waited] = explode(' ', $results[1]);
    expect($results[0])->toBe('released')
        ->and($code)->toBe('0')
        ->and((float) $waited)->toBeGreaterThan(0.5);
});

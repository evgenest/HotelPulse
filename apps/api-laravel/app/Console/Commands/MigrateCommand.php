<?php

declare(strict_types=1);

namespace App\Console\Commands;

use Illuminate\Console\Command;
use Illuminate\Support\Facades\DB;

/**
 * Applies migrations once even when several copies start together.
 *
 * `migrate --isolated` locks through the Laravel cache, which is per-process
 * here (array store), so it would not stop two containers. Instead a
 * transaction-scoped advisory lock is held in an open transaction on its own
 * connection while the migrations run on another. That works through a pooler
 * in transaction mode (Neon's PgBouncer keeps the server connection for an
 * open transaction), and Postgres releases the lock when the transaction ends,
 * also if this process dies. A session lock (pg_advisory_lock) could stay
 * behind on a pooled server connection and block the next start (ADR-0011).
 * Same approach as the .NET DatabaseMigrator.
 */
final class MigrateCommand extends Command
{
    private const int AdvisoryLockKey = 48291002;

    protected $signature = 'hotelpulse:migrate';

    protected $description = 'Apply database migrations under a Postgres advisory lock';

    public function handle(): int
    {
        $lock = DB::connection('pgsql_lock');
        $lock->beginTransaction();

        try {
            $lock->select('SELECT pg_advisory_xact_lock(?)', [self::AdvisoryLockKey]);

            return $this->call('migrate', ['--force' => true, '--database' => 'pgsql_direct']);
        } finally {
            // Ending the transaction releases the lock; nothing was written in it.
            $lock->rollBack();
            DB::purge('pgsql_lock');
        }
    }
}

<?php

declare(strict_types=1);

namespace App\Console\Commands;

use Illuminate\Console\Command;
use Illuminate\Support\Facades\DB;

/**
 * Applies migrations once even when several copies start together.
 *
 * `migrate --isolated` locks through the Laravel cache, which is per-process
 * here (array store), so it would not stop two containers. Like the .NET
 * DatabaseMigrator, this takes a session advisory lock in Postgres on the
 * direct (non-pooled) connection: PgBouncer transaction mode would drop a
 * session lock between statements.
 */
final class MigrateCommand extends Command
{
    private const int AdvisoryLockKey = 48291002;

    protected $signature = 'hotelpulse:migrate';

    protected $description = 'Apply database migrations under a Postgres advisory lock';

    public function handle(): int
    {
        $connection = DB::connection('pgsql_direct');
        $connection->statement('SELECT pg_advisory_lock(?)', [self::AdvisoryLockKey]);

        try {
            return $this->call('migrate', ['--force' => true, '--database' => 'pgsql_direct']);
        } finally {
            $connection->statement('SELECT pg_advisory_unlock(?)', [self::AdvisoryLockKey]);
        }
    }
}

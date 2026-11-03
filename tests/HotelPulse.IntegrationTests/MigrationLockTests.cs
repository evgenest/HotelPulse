using HotelPulse.Infrastructure.Postgres;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace HotelPulse.IntegrationTests;

/// <summary>ADR-0011: migrations run once under a lock that cannot be left behind.</summary>
[Collection("stack")]
public sealed class MigrationLockTests(HotelPulseStack stack)
{
    [Fact(Timeout = 30_000)]
    public async Task Parallel_migrators_all_finish_and_leave_no_advisory_lock()
    {
        await using var dataSource = NpgsqlDataSource.Create(stack.PostgresUri);
        var before = await stack.ScalarAsync("SELECT count(*) FROM schema_migrations");

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            new DatabaseMigrator(dataSource, NullLogger<DatabaseMigrator>.Instance).ApplyAsync()));

        Assert.Equal(before, await stack.ScalarAsync("SELECT count(*) FROM schema_migrations"));
        Assert.Equal("0", await stack.ScalarAsync("SELECT count(*) FROM pg_locks WHERE locktype = 'advisory'"));
    }

    [Fact(Timeout = 30_000)]
    public async Task A_migrator_waits_for_the_lock_and_runs_once_it_is_released()
    {
        await using var dataSource = NpgsqlDataSource.Create(stack.PostgresUri);
        await using var holder = await dataSource.OpenConnectionAsync();
        await using var held = await holder.BeginTransactionAsync();
        await using (var take = new NpgsqlCommand("SELECT pg_advisory_xact_lock(48291001)", holder, held))
            await take.ExecuteNonQueryAsync();

        var migrating = new DatabaseMigrator(dataSource, NullLogger<DatabaseMigrator>.Instance).ApplyAsync();
        await Task.Delay(500);
        Assert.False(migrating.IsCompleted);

        await held.RollbackAsync();
        await migrating;
        Assert.True(migrating.IsCompletedSuccessfully);
    }
}

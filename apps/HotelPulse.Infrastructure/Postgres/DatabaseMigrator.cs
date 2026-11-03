using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HotelPulse.Infrastructure.Postgres;

public sealed class DatabaseMigrator(NpgsqlDataSource dataSource, ILogger<DatabaseMigrator> logger)
{
    private const int AdvisoryLockKey = 48291001;

    /// <summary>
    /// Applies pending migrations once, even when several copies start together.
    /// The lock is transaction-scoped and lives in an open transaction on its own
    /// connection, so it holds through a pooler in transaction mode (Neon's
    /// PgBouncer pins a server connection to an open transaction) and is released
    /// by Postgres when that transaction ends, also if this process dies.
    /// A session lock (pg_advisory_lock) could stay behind on a pooled server
    /// connection and block the next start forever (ADR-0011).
    /// </summary>
    public async Task ApplyAsync(CancellationToken ct = default)
    {
        await using var lockConnection = await OpenAsync(ct);
        await using var lockTx = await lockConnection.BeginTransactionAsync(ct);
        await lockConnection.ExecuteAsync("SELECT pg_advisory_xact_lock(@key)", new { key = AdvisoryLockKey }, lockTx);
        try
        {
            await using var connection = await OpenAsync(ct);
            await ApplyPendingAsync(connection, ct);
        }
        finally
        {
            // Ending the transaction releases the lock; nothing else was written in it.
            await lockTx.RollbackAsync(CancellationToken.None);
        }
    }

    private async Task ApplyPendingAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                id text PRIMARY KEY,
                applied_at timestamptz NOT NULL DEFAULT now()
            );
            """);

        var applied = (await connection.QueryAsync<string>("SELECT id FROM schema_migrations"))
            .ToHashSet(StringComparer.Ordinal);

        var assembly = typeof(DatabaseMigrator).Assembly;
        var names = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal);

        foreach (var name in names)
        {
            var fileName = FileName(name);
            if (applied.Contains(fileName))
                continue;

            await using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Missing migration resource {name}.");
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(ct);

            await using var tx = await connection.BeginTransactionAsync(ct);
            await connection.ExecuteAsync(sql, transaction: tx);
            await connection.ExecuteAsync(
                "INSERT INTO schema_migrations (id) VALUES (@id)",
                new { id = fileName },
                tx);
            await tx.CommitAsync(ct);
            logger.LogInformation("Applied migration {Migration}", fileName);
        }
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= StartupConnections.RetryDelays.Length + 1; attempt++)
        {
            try
            {
                return await dataSource.OpenConnectionAsync(ct);
            }
            catch (Exception ex) when (StartupConnections.IsTransient(ex) && attempt <= StartupConnections.RetryDelays.Length)
            {
                last = ex;
                var delay = StartupConnections.RetryDelays[attempt - 1];
                logger.LogWarning(
                    ex,
                    "Postgres is not ready (attempt {Attempt}); retrying in {DelaySeconds}s",
                    attempt,
                    delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
        }

        throw last ?? new InvalidOperationException("Postgres connection failed.");
    }

    private static string FileName(string resourceName)
    {
        const string marker = ".Migrations.";
        var index = resourceName.IndexOf(marker, StringComparison.Ordinal);
        return index >= 0 ? resourceName[(index + marker.Length)..] : resourceName;
    }
}

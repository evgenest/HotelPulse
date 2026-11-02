using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HotelPulse.Infrastructure.Postgres;

public sealed class DatabaseMigrator(NpgsqlDataSource dataSource, ILogger<DatabaseMigrator> logger)
{
    private const int AdvisoryLockKey = 48291001;

    public async Task ApplyAsync(CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync("SELECT pg_advisory_lock(@key)", new { key = AdvisoryLockKey });
        try
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
        finally
        {
            await connection.ExecuteAsync("SELECT pg_advisory_unlock(@key)", new { key = AdvisoryLockKey });
        }
    }

    private static string FileName(string resourceName)
    {
        const string marker = ".Migrations.";
        var index = resourceName.IndexOf(marker, StringComparison.Ordinal);
        return index >= 0 ? resourceName[(index + marker.Length)..] : resourceName;
    }
}

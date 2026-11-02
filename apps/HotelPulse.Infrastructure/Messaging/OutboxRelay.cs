using System.Text.Json;
using Dapper;
using HotelPulse.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HotelPulse.Infrastructure.Messaging;

/// <summary>
/// Publishes booking messages that were committed in the same transaction as the
/// booking row. If the broker is down, the row stays unpublished and this loop
/// retries. The worker treats a second delivery as a no-op.
/// </summary>
public sealed class OutboxRelay(
    NpgsqlDataSource dataSource,
    BookingPublisher publisher,
    ILogger<OutboxRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await PublishBatchAsync(stoppingToken);
                if (published == 0)
                    await Task.Delay(200, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox relay failed; retrying");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task<int> PublishBatchAsync(CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var rows = (await connection.QueryAsync<OutboxRow>(
            """
            SELECT id, payload::text AS payload
            FROM outbox
            WHERE published_at IS NULL
            ORDER BY id
            FOR UPDATE SKIP LOCKED
            LIMIT 20
            """,
            transaction: tx)).ToList();

        foreach (var row in rows)
        {
            var message = JsonSerializer.Deserialize<BookingMessage>(row.Payload, HotelPulseJson.Serializer)
                ?? throw new PermanentProcessingException($"Outbox row {row.Id} had an empty payload.");
            await publisher.PublishAsync(message, ct);
            await connection.ExecuteAsync(
                "UPDATE outbox SET published_at = now() WHERE id = @id",
                new { id = row.Id },
                tx);
        }

        await tx.CommitAsync(ct);
        return rows.Count;
    }

    private sealed class OutboxRow
    {
        public long Id { get; set; }
        public string Payload { get; set; } = "";
    }
}

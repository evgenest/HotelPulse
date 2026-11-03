using System.Text.Json;
using Dapper;
using HotelPulse.Domain;
using Npgsql;

namespace HotelPulse.Infrastructure.Postgres;

public sealed class BookingRepository(NpgsqlDataSource dataSource) : IBookingStore, IBookingCommands
{
    private const string BookingSelect =
        """
        SELECT id,
               hotel_id,
               hotel_name,
               room_id,
               room_type,
               guest_name,
               check_in,
               check_out,
               nights,
               total,
               status,
               confirmation_code,
               rejection_reason,
               created_at,
               cancelled_at,
               events::text AS events
        FROM bookings
        """;

    public async Task<Booking?> FindAsync(string id, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<BookingRow>(
            BookingSelect + " WHERE id = @id",
            new { id });
        return row is null ? null : Map(row);
    }

    public async Task UpdateEventsAsync(string id, IReadOnlyList<BookingEvent> events, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(
            "UPDATE bookings SET events = @events::jsonb WHERE id = @id AND status = 'pending'",
            new { id, events = Serialize(events) });
    }

    public async Task<bool> TryCompleteAsync(string id, BookingCompletion completion, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var updated = await connection.ExecuteAsync(
            """
            UPDATE bookings
            SET status = @status,
                confirmation_code = @confirmationCode,
                rejection_reason = @rejectionReason,
                events = @events::jsonb
            WHERE id = @id
              AND status = 'pending'
            """,
            new
            {
                id,
                status = completion.Status,
                confirmationCode = completion.ConfirmationCode,
                rejectionReason = completion.RejectionReason,
                events = Serialize(completion.Events),
            });
        return updated == 1;
    }

    public async Task<CreateBookingResult> CreateAsync(
        Booking booking,
        string? idempotencyKey,
        string requestHash,
        CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        if (idempotencyKey is not null)
        {
            var existing = await FindReplayAsync(connection, null, idempotencyKey, requestHash);
            if (existing is not null)
                return existing;
        }

        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            if (idempotencyKey is not null)
            {
                var existing = await FindReplayAsync(connection, tx, idempotencyKey, requestHash);
                if (existing is not null)
                {
                    await tx.CommitAsync(ct);
                    return existing;
                }
            }

            await connection.ExecuteAsync(
                """
                INSERT INTO bookings (
                    id, hotel_id, room_id, hotel_name, room_type, guest_name,
                    check_in, check_out, nights, total, status,
                    confirmation_code, rejection_reason, created_at, events)
                VALUES (
                    @Id, @HotelId, @RoomId, @HotelName, @RoomType, @GuestName,
                    @CheckIn, @CheckOut, @Nights, @Total, @Status,
                    @ConfirmationCode, @RejectionReason, @CreatedAt, @Events::jsonb)
                """,
                new
                {
                    booking.Id,
                    booking.HotelId,
                    booking.RoomId,
                    booking.HotelName,
                    booking.RoomType,
                    booking.GuestName,
                    booking.CheckIn,
                    booking.CheckOut,
                    booking.Nights,
                    booking.Total,
                    booking.Status,
                    booking.ConfirmationCode,
                    booking.RejectionReason,
                    CreatedAt = DateTime.SpecifyKind(booking.CreatedAt, DateTimeKind.Utc),
                    Events = Serialize(booking.Events),
                },
                tx);

            var message = new BookingMessage(booking.Id, booking.HotelId, booking.RoomId, booking.CreatedAt);
            await connection.ExecuteAsync(
                """
                INSERT INTO outbox (booking_id, payload)
                VALUES (@bookingId, @payload::jsonb)
                """,
                new
                {
                    bookingId = booking.Id,
                    payload = JsonSerializer.Serialize(message, HotelPulseJson.Serializer),
                },
                tx);

            if (idempotencyKey is not null)
            {
                await connection.ExecuteAsync(
                    """
                    INSERT INTO idempotency_keys (idempotency_key, request_hash, booking_id)
                    VALUES (@idempotencyKey, @requestHash, @bookingId)
                    """,
                    new { idempotencyKey, requestHash, bookingId = booking.Id },
                    tx);
            }

            await tx.CommitAsync(ct);
            return new CreateBookingResult(booking, Replay: false);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ExclusionViolation)
        {
            await tx.RollbackAsync(ct);
            throw new RoomUnavailableException();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation
                                           && ex.ConstraintName == "idempotency_keys_pkey"
                                           && idempotencyKey is not null)
        {
            await tx.RollbackAsync(ct);
            var existing = await FindReplayAsync(connection, null, idempotencyKey, requestHash);
            if (existing is not null)
                return existing;
            throw;
        }
    }

    public async Task<Booking?> TryCancelAsync(
        string id,
        DateOnly today,
        BookingEvent cancelEvent,
        CancellationToken ct)
    {
        // The timeline is rebuilt from the stored value, not from the caller's
        // copy: the worker may have appended steps since the caller read the row
        // (the status is still pending, so the WHERE clause still matches).
        // Same result as BookingTimeline.Cancelled, computed inside the UPDATE.
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<BookingRow>(
            """
            UPDATE bookings
            SET status = 'cancelled',
                cancelled_at = now(),
                events = (
                    SELECT coalesce(jsonb_agg(e || '{"current": false}'::jsonb ORDER BY ord), '[]'::jsonb)
                    FROM jsonb_array_elements(bookings.events) WITH ORDINALITY AS t(e, ord)
                ) || jsonb_build_array(@cancelEvent::jsonb)
            WHERE id = @id
              AND status IN ('pending', 'confirmed')
              AND check_in > @today
            RETURNING id,
                      hotel_id,
                      hotel_name,
                      room_id,
                      room_type,
                      guest_name,
                      check_in,
                      check_out,
                      nights,
                      total,
                      status,
                      confirmation_code,
                      rejection_reason,
                      created_at,
                      cancelled_at,
                      events::text AS events
            """,
            new { id, today, cancelEvent = JsonSerializer.Serialize(cancelEvent, HotelPulseJson.Serializer) });
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<Booking>> ListRecentAsync(int limit, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<BookingRow>(
            BookingSelect + " ORDER BY created_at DESC LIMIT @limit",
            new { limit });
        return rows.Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<Booking>> GetManyAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return [];

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<BookingRow>(
            BookingSelect + " WHERE id = ANY(@ids)",
            new { ids = ids.ToArray() });
        var byId = rows.Select(Map).ToDictionary(b => b.Id, StringComparer.Ordinal);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
    }

    private static async Task<CreateBookingResult?> FindReplayAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? tx,
        string idempotencyKey,
        string requestHash)
    {
        var stored = await connection.QuerySingleOrDefaultAsync<IdempotencyRow>(
            """
            SELECT request_hash, booking_id
            FROM idempotency_keys
            WHERE idempotency_key = @idempotencyKey
            """,
            new { idempotencyKey },
            tx);

        if (stored is null)
            return null;

        if (!string.Equals(stored.RequestHash, requestHash, StringComparison.Ordinal))
            throw new IdempotencyConflictException();

        var row = await connection.QuerySingleAsync<BookingRow>(
            BookingSelect + " WHERE id = @id",
            new { id = stored.BookingId },
            tx);
        return new CreateBookingResult(Map(row), Replay: true);
    }

    private static string Serialize(IReadOnlyList<BookingEvent> events) =>
        JsonSerializer.Serialize(events, HotelPulseJson.Serializer);

    private static Booking Map(BookingRow row)
    {
        var events = string.IsNullOrWhiteSpace(row.Events)
            ? []
            : JsonSerializer.Deserialize<List<BookingEvent>>(row.Events, HotelPulseJson.Serializer) ?? [];

        var createdAt = row.CreatedAt.Kind == DateTimeKind.Utc
            ? row.CreatedAt
            : DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc);

        return new Booking(
            row.Id,
            row.HotelId,
            row.HotelName,
            row.RoomId,
            row.RoomType,
            row.GuestName,
            row.CheckIn,
            row.CheckOut,
            row.Nights,
            row.Total,
            row.Status,
            row.ConfirmationCode,
            row.RejectionReason,
            createdAt,
            events,
            row.CancelledAt is { } cancelledAt ? DateTime.SpecifyKind(cancelledAt, DateTimeKind.Utc) : null);
    }

    private sealed class IdempotencyRow
    {
        public string RequestHash { get; set; } = "";
        public string BookingId { get; set; } = "";
    }

    private sealed class BookingRow
    {
        public string Id { get; set; } = "";
        public string HotelId { get; set; } = "";
        public string HotelName { get; set; } = "";
        public string RoomId { get; set; } = "";
        public string RoomType { get; set; } = "";
        public string GuestName { get; set; } = "";
        public DateOnly CheckIn { get; set; }
        public DateOnly CheckOut { get; set; }
        public int Nights { get; set; }
        public decimal Total { get; set; }
        public string Status { get; set; } = "";
        public string? ConfirmationCode { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string Events { get; set; } = "[]";
    }
}

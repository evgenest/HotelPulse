using System.Globalization;
using HotelPulse.Domain;
using HotelPulse.Infrastructure.Caching;
using HotelPulse.Infrastructure.Postgres;
using Npgsql;

public static class HotelPulseEndpoints
{
    private static readonly string Version =
        typeof(HotelPulseEndpoints).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    public static void Map(WebApplication app)
    {
        app.MapGet("/health", Health);
        app.MapGet("/api/hotels", ListHotels);
        app.MapGet("/api/hotels/{id}", GetHotel);
        app.MapGet("/api/hotels/{id}/availability", Availability);
        app.MapPost("/api/bookings", CreateBooking);
        app.MapGet("/api/bookings", ListBookings);
        app.MapGet("/api/bookings/{id}", GetBooking);
        app.MapPost("/api/bookings/{id}/cancel", CancelBooking);
    }

    private static async Task<IResult> Health(NpgsqlDataSource db, CancellationToken ct)
    {
        try
        {
            await using var connection = await db.OpenConnectionAsync(ct);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(ct);
            return Results.Ok(new { status = "ok", version = Version, time = DateTime.UtcNow });
        }
        catch
        {
            return Results.Json(
                new { status = "unavailable", version = Version, time = DateTime.UtcNow },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> ListHotels(HotelCatalog catalog, HttpContext http, CancellationToken ct)
    {
        var result = await catalog.ListAsync(ct);
        return Cached(http, result.CacheStatus, result.Value);
    }

    private static async Task<IResult> GetHotel(string id, HotelCatalog catalog, HttpContext http, CancellationToken ct)
    {
        var result = await catalog.GetAsync(id, ct);
        return result is null
            ? Results.NotFound(new { error = "Hotel not found." })
            : Cached(http, result.CacheStatus, result.Value);
    }

    private static async Task<IResult> Availability(
        string id,
        string? checkIn,
        string? checkOut,
        HotelCatalog catalog,
        HttpContext http,
        CancellationToken ct)
    {
        if (!TryStay(checkIn, checkOut, out var arrival, out var departure, out var error))
            return Results.BadRequest(new { errors = new[] { error } });

        var result = await catalog.AvailabilityAsync(id, arrival, departure, ct);
        if (result is null)
            return Results.NotFound(new { error = "Hotel not found." });

        return Cached(http, result.CacheStatus, new
        {
            hotelId = id,
            checkIn = arrival,
            checkOut = departure,
            rooms = result.Value,
        });
    }

    private static async Task<IResult> CreateBooking(
        CreateBookingRequest? request,
        HotelRepository hotels,
        IBookingCommands bookings,
        RedisHotelCache cache,
        HttpRequest http,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.HotelId))
            return Results.BadRequest(new { errors = new[] { new BookingValidationError("body", "Request body is required.") } });

        var hotel = await hotels.GetAsync(request.HotelId, ct);
        if (hotel is null)
            return Results.NotFound(new { error = "Hotel not found." });

        var room = hotel.Rooms.FirstOrDefault(candidate => candidate.Id == request.RoomId);
        if (room is null)
            return Results.NotFound(new { error = "Room not found." });

        var command = new CreateBookingCommand(
            request.HotelId,
            request.RoomId,
            request.GuestName,
            request.CheckIn,
            request.CheckOut,
            request.Nights,
            request.Total);
        var errors = BookingValidator.Validate(command, room, DateOnly.FromDateTime(DateTime.UtcNow));
        if (errors.Count > 0)
            return Results.BadRequest(new { errors });

        var checkIn = DateOnly.ParseExact(request.CheckIn, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var checkOut = DateOnly.ParseExact(request.CheckOut, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var nights = checkOut.DayNumber - checkIn.DayNumber;
        var now = DateTime.UtcNow;
        var booking = new Booking(
            "bkg_" + Guid.NewGuid().ToString("N")[..12],
            hotel.Id,
            hotel.Name,
            room.Id,
            room.Type,
            request.GuestName.Trim(),
            checkIn,
            checkOut,
            nights,
            room.Price * nights,
            BookingStatuses.Pending,
            null,
            null,
            now,
            BookingTimeline.Initial(now));

        var idempotencyKey = http.Headers.TryGetValue("Idempotency-Key", out var header)
            ? header.ToString().Trim()
            : "";
        if (idempotencyKey.Length > 200)
            return Results.BadRequest(new { errors = new[] { new BookingValidationError("Idempotency-Key", "Idempotency-Key must be 200 characters or fewer.") } });

        try
        {
            var created = await bookings.CreateAsync(
                booking,
                string.IsNullOrEmpty(idempotencyKey) ? null : idempotencyKey,
                IdempotencyHash.Compute(command),
                ct);

            if (!created.Replay)
                await cache.InvalidateHotelAsync(created.Booking.HotelId);

            return Results.Accepted(
                $"/api/bookings/{created.Booking.Id}",
                new { id = created.Booking.Id, status = created.Booking.Status });
        }
        catch (RoomUnavailableException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
        catch (IdempotencyConflictException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }

    private static async Task<IResult> ListBookings(string? ids, IBookingCommands bookings, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ids))
            return Results.Ok(await bookings.ListRecentAsync(20, ct));

        var requested = ids
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .Take(20)
            .ToArray();

        return Results.Ok(await bookings.GetManyAsync(requested, ct));
    }

    private static async Task<IResult> GetBooking(string id, IBookingStore store, CancellationToken ct)
    {
        var booking = await store.FindAsync(id, ct);
        return booking is null
            ? Results.NotFound(new { error = "Booking not found." })
            : Results.Ok(booking);
    }

    private static async Task<IResult> CancelBooking(
        string id,
        IBookingStore store,
        IBookingCommands bookings,
        RedisHotelCache cache,
        CancellationToken ct)
    {
        // Read, decide, then write with the same rules in the WHERE clause.
        // If another request changed the row in between, decide again on the
        // fresh row. Two rounds are enough: after a failed write the row is
        // terminal or past check-in, and neither of those can change again.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var booking = await store.FindAsync(id, ct);
            if (booking is null)
                return Results.NotFound(new { error = "Booking not found." });

            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(now);
            var decision = BookingCancellation.Decide(booking.Status, booking.CheckIn, today);

            switch (decision)
            {
                case CancellationDecision.AlreadyCancelled:
                    return Results.Ok(booking);
                case CancellationDecision.NotCancellable:
                case CancellationDecision.TooLate:
                    return Results.Conflict(new { error = BookingCancellation.Error(decision) });
            }

            var cancelled = await bookings.TryCancelAsync(
                id,
                today,
                BookingTimeline.Cancelled(booking.Events, now),
                ct);
            if (cancelled is null)
                continue;

            await cache.InvalidateHotelAsync(cancelled.HotelId);
            return Results.Ok(cancelled);
        }

        return Results.Conflict(new { error = "The booking changed while it was being cancelled. Try again." });
    }

    private static IResult Cached<T>(HttpContext http, string cacheStatus, T body)
    {
        http.Response.Headers["X-Cache"] = cacheStatus;
        return Results.Ok(body);
    }

    private static bool TryStay(
        string? checkIn,
        string? checkOut,
        out DateOnly arrival,
        out DateOnly departure,
        out BookingValidationError error)
    {
        arrival = default;
        departure = default;
        if (!DateOnly.TryParseExact(checkIn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out arrival))
        {
            error = new("checkIn", "Check-in must be a date in yyyy-MM-dd format.");
            return false;
        }

        if (!DateOnly.TryParseExact(checkOut, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out departure))
        {
            error = new("checkOut", "Check-out must be a date in yyyy-MM-dd format.");
            return false;
        }

        if (departure <= arrival)
        {
            error = new("checkOut", "Check-out must be after check-in.");
            return false;
        }

        error = null!;
        return true;
    }
}

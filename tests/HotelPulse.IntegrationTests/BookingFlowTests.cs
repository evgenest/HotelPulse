using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HotelPulse.IntegrationTests;

[Collection("stack")]
public sealed class BookingFlowTests(HotelPulseStack stack) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await stack.ResetAsync();
        await stack.StartWorkerAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(Timeout = 30_000)]
    public async Task Creating_a_booking_returns_202_and_the_worker_confirms_it()
    {
        var (checkIn, checkOut) = Stay(40);
        var created = await stack.PostBookingAsync(checkIn, checkOut);
        var id = created.GetProperty("id").GetString();

        Assert.StartsWith("bkg_", id);
        Assert.Equal("pending", created.GetProperty("status").GetString());

        var booking = await stack.WaitForTerminalAsync(id!);
        Assert.Equal("confirmed", booking.GetProperty("status").GetString());
        Assert.Matches("^HP-[0-9A-F]{6}$", booking.GetProperty("confirmationCode").GetString());
        Assert.Equal(5, booking.GetProperty("events").GetArrayLength());
        Assert.Contains("postgres", booking.GetProperty("events")[3].GetProperty("label").GetString());
    }

    [Fact(Timeout = 30_000)]
    public async Task An_overlapping_stay_is_rejected_while_the_first_hold_is_pending()
    {
        var (checkIn, checkOut) = Stay(50);
        await stack.PostBookingAsync(checkIn, checkOut);

        var response = await stack.PostRawAsync(new
        {
            hotelId = "h_porter",
            roomId = "r1",
            guestName = "Boris Keller",
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.AddDays(1).ToString("yyyy-MM-dd"),
            nights = checkOut.AddDays(1).DayNumber - checkIn.DayNumber,
            total = 150m * (checkOut.AddDays(1).DayNumber - checkIn.DayNumber),
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("already held", body.GetProperty("error").GetString());
    }

    [Fact(Timeout = 30_000)]
    public async Task Adjacent_stays_are_both_accepted()
    {
        var (checkIn, checkOut) = Stay(60);
        var first = await stack.PostBookingAsync(checkIn, checkOut);
        var second = await stack.PostBookingAsync(checkOut, checkOut.AddDays(2));

        Assert.NotEqual(first.GetProperty("id").GetString(), second.GetProperty("id").GetString());
        Assert.Equal("pending", second.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_total_that_does_not_match_the_room_is_rejected()
    {
        var (checkIn, checkOut) = Stay(70);
        var response = await stack.PostRawAsync(new
        {
            hotelId = "h_porter",
            roomId = "r1",
            guestName = "Anna Schmidt",
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            nights = 3,
            total = 1,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_hotel_is_not_found()
    {
        var (checkIn, checkOut) = Stay(80);
        var response = await stack.PostRawAsync(new
        {
            hotelId = "h_missing",
            roomId = "r1",
            guestName = "Anna Schmidt",
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            nights = 3,
            total = 450,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Replaying_an_idempotency_key_returns_the_same_booking()
    {
        var (checkIn, checkOut) = Stay(90);
        var first = await stack.PostBookingAsync(checkIn, checkOut, idempotencyKey: "stay-90");
        var second = await stack.PostBookingAsync(checkIn, checkOut, idempotencyKey: "stay-90");

        Assert.Equal(first.GetProperty("id").GetString(), second.GetProperty("id").GetString());

        var conflict = await stack.PostRawAsync(new
        {
            hotelId = "h_porter",
            roomId = "r1",
            guestName = "Someone Else",
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            nights = 3,
            total = 450,
        }, idempotencyKey: "stay-90");

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task The_catalog_lists_the_seeded_hotels()
    {
        var response = await stack.Client.GetAsync("/api/hotels");
        response.EnsureSuccessStatusCode();
        var hotels = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(6, hotels.GetArrayLength());
        Assert.Equal("MISS", CacheHeader(response));

        var again = await stack.Client.GetAsync("/api/hotels");
        Assert.Equal("HIT", CacheHeader(again));
    }

    private static string CacheHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Cache", out var values) ? values.Single() : "";

    private static (DateOnly CheckIn, DateOnly CheckOut) Stay(int daysAhead)
    {
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(daysAhead);
        return (checkIn, checkIn.AddDays(3));
    }
}

[Collection("stack")]
public sealed class AvailabilityCacheTests(HotelPulseStack stack) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await stack.ResetAsync();
        await stack.StartWorkerAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(Timeout = 30_000)]
    public async Task Booking_a_room_invalidates_availability_and_the_hotel_list()
    {
        var (checkIn, checkOut) = (DateOnly.FromDateTime(DateTime.UtcNow).AddDays(120), DateOnly.FromDateTime(DateTime.UtcNow).AddDays(123));
        var query = $"/api/hotels/h_porter/availability?checkIn={checkIn:yyyy-MM-dd}&checkOut={checkOut:yyyy-MM-dd}";

        var first = await stack.Client.GetAsync(query);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MISS", CacheHeader(first));
        Assert.True(Room(firstBody, "r1").GetProperty("available").GetBoolean());

        var cached = await stack.Client.GetAsync(query);
        Assert.Equal("HIT", CacheHeader(cached));

        var list = await stack.Client.GetAsync("/api/hotels");
        Assert.Equal("MISS", CacheHeader(list));
        var listHit = await stack.Client.GetAsync("/api/hotels");
        Assert.Equal("HIT", CacheHeader(listHit));

        await stack.PostBookingAsync(checkIn, checkOut);

        var after = await stack.Client.GetAsync(query);
        var afterBody = await after.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MISS", CacheHeader(after));
        Assert.False(Room(afterBody, "r1").GetProperty("available").GetBoolean());
        Assert.True(Room(afterBody, "r2").GetProperty("available").GetBoolean());

        var listAfter = await stack.Client.GetAsync("/api/hotels");
        Assert.Equal("MISS", CacheHeader(listAfter));
    }

    [Fact]
    public async Task The_availability_index_and_exclusion_constraint_exist()
    {
        var indexes = await stack.ScalarAsync(
            "SELECT string_agg(indexname, ',') FROM pg_indexes WHERE tablename = 'bookings'");
        Assert.Contains("bookings_availability_idx", indexes);
        Assert.Contains("bookings_recent_idx", indexes);

        var constraint = await stack.ScalarAsync(
            "SELECT conname FROM pg_constraint WHERE conname = 'bookings_no_overlapping_stay'");
        Assert.Equal("bookings_no_overlapping_stay", constraint);
    }

    private static JsonElement Room(JsonElement body, string id) =>
        body.GetProperty("rooms").EnumerateArray().Single(room => room.GetProperty("id").GetString() == id);

    private static string CacheHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Cache", out var values) ? values.Single() : "";
}

[Collection("stack")]
public sealed class DurableQueueTests(HotelPulseStack stack) : IAsyncLifetime
{
    public Task InitializeAsync() => stack.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(Timeout = 30_000)]
    public async Task The_booking_stays_pending_until_the_worker_consumes_the_durable_message()
    {
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(150);
        var created = await stack.PostBookingAsync(checkIn, checkIn.AddDays(2));
        var id = created.GetProperty("id").GetString()!;

        await Task.Delay(TimeSpan.FromSeconds(1));
        var pending = await stack.GetBookingAsync(id);
        Assert.Equal("pending", pending.GetProperty("status").GetString());

        await stack.StartWorkerAsync();
        var done = await stack.WaitForTerminalAsync(id);
        Assert.Equal("confirmed", done.GetProperty("status").GetString());
    }
}

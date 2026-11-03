using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HotelPulse.IntegrationTests;

/// <summary>
/// The cases listed in docs/specs/booking-cancellation.md, against real
/// Postgres, Redis and RabbitMQ.
/// </summary>
[Collection("stack")]
public sealed class CancellationTests(HotelPulseStack stack) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await stack.ResetAsync();
        await stack.StartWorkerAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(Timeout = 30_000)]
    public async Task A_confirmed_booking_can_be_cancelled()
    {
        var (checkIn, checkOut) = Stay(200);
        var id = await ConfirmedBookingAsync(checkIn, checkOut);

        var response = await CancelAsync(id);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("cancelled", body.GetProperty("status").GetString());
        Assert.True(body.TryGetProperty("cancelledAt", out _));
        Assert.Matches("^HP-[0-9A-F]{6}$", body.GetProperty("confirmationCode").GetString());
        Assert.Equal("api → booking cancelled", body.GetProperty("events")[^1].GetProperty("label").GetString());

        var stored = await stack.GetBookingAsync(id);
        Assert.Equal("cancelled", stored.GetProperty("status").GetString());
    }

    [Fact(Timeout = 30_000)]
    public async Task Cancelling_releases_the_room_for_the_same_dates()
    {
        var (checkIn, checkOut) = Stay(210);
        var id = await ConfirmedBookingAsync(checkIn, checkOut);
        var query = $"/api/hotels/h_porter/availability?checkIn={checkIn:yyyy-MM-dd}&checkOut={checkOut:yyyy-MM-dd}";

        var held = await stack.Client.GetFromJsonAsync<JsonElement>(query);
        Assert.False(Room(held, "r1").GetProperty("available").GetBoolean());

        (await CancelAsync(id)).EnsureSuccessStatusCode();

        var released = await stack.Client.GetAsync(query);
        var releasedBody = await released.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MISS", CacheHeader(released));
        Assert.True(Room(releasedBody, "r1").GetProperty("available").GetBoolean());

        var rebooked = await stack.PostBookingAsync(checkIn, checkOut, guestName: "Boris Keller");
        Assert.Equal("pending", rebooked.GetProperty("status").GetString());
    }

    [Fact(Timeout = 30_000)]
    public async Task A_second_cancel_succeeds_and_changes_nothing()
    {
        var (checkIn, checkOut) = Stay(220);
        var id = await ConfirmedBookingAsync(checkIn, checkOut);

        var first = await (await CancelAsync(id)).Content.ReadFromJsonAsync<JsonElement>();
        var secondResponse = await CancelAsync(id);
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(
            first.GetProperty("cancelledAt").GetString(),
            second.GetProperty("cancelledAt").GetString());
        Assert.Equal(
            first.GetProperty("events").GetArrayLength(),
            second.GetProperty("events").GetArrayLength());
    }

    [Fact]
    public async Task An_unknown_booking_is_not_found()
    {
        var response = await CancelAsync("bkg_missing");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_booking_cannot_be_cancelled_on_the_check_in_day()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var id = await ConfirmedBookingAsync(today, today.AddDays(2));

        var response = await CancelAsync(id);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("before the check-in date", body.GetProperty("error").GetString());
        Assert.Equal("confirmed", (await stack.GetBookingAsync(id)).GetProperty("status").GetString());
    }

    private async Task<string> ConfirmedBookingAsync(DateOnly checkIn, DateOnly checkOut)
    {
        var created = await stack.PostBookingAsync(checkIn, checkOut);
        var id = created.GetProperty("id").GetString()!;
        var booking = await stack.WaitForTerminalAsync(id);
        Assert.Equal("confirmed", booking.GetProperty("status").GetString());
        return id;
    }

    private Task<HttpResponseMessage> CancelAsync(string id) =>
        stack.Client.PostAsync($"/api/bookings/{id}/cancel", content: null);

    private static JsonElement Room(JsonElement body, string id) =>
        body.GetProperty("rooms").EnumerateArray().Single(room => room.GetProperty("id").GetString() == id);

    private static string CacheHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Cache", out var values) ? values.Single() : "";

    private static (DateOnly CheckIn, DateOnly CheckOut) Stay(int daysAhead)
    {
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(daysAhead);
        return (checkIn, checkIn.AddDays(3));
    }
}

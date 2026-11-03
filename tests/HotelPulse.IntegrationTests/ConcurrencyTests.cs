using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HotelPulse.Domain;
using HotelPulse.Infrastructure.Postgres;
using Npgsql;

namespace HotelPulse.IntegrationTests;

/// <summary>
/// Requests that really run at the same time. The guarantees under test live
/// in SQL (exclusion constraint, compare-and-set, primary keys), so these
/// tests fire requests in parallel instead of one after another.
/// </summary>
[Collection("stack")]
public sealed class ConcurrencyTests(HotelPulseStack stack) : IAsyncLifetime
{
    private const int Parallel = 10;

    public Task InitializeAsync() => stack.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(Timeout = 30_000)]
    public async Task Parallel_bookings_of_the_same_room_and_dates_accept_exactly_one()
    {
        var (checkIn, checkOut) = Stay(300);

        var responses = await Task.WhenAll(Enumerable.Range(0, Parallel).Select(i =>
            stack.PostRawAsync(Body(checkIn, checkOut, $"Guest {i}"))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Accepted));
        Assert.Equal(Parallel - 1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal("1", await stack.ScalarAsync("SELECT count(*) FROM bookings"));
        Assert.Equal("1", await stack.ScalarAsync("SELECT count(*) FROM outbox"));
    }

    [Fact(Timeout = 30_000)]
    public async Task Parallel_requests_with_one_idempotency_key_create_one_booking()
    {
        var (checkIn, checkOut) = Stay(310);
        var body = Body(checkIn, checkOut, "Anna Schmidt");

        var responses = await Task.WhenAll(Enumerable.Range(0, Parallel).Select(_ =>
            stack.PostRawAsync(body, idempotencyKey: "parallel-key-1")));

        Assert.All(responses, r => Assert.True(r.StatusCode == HttpStatusCode.Accepted, $"{(int)r.StatusCode}: {r.Content.ReadAsStringAsync().Result}"));
        var ids = new HashSet<string>();
        foreach (var response in responses)
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            ids.Add(json.GetProperty("id").GetString()!);
        }

        Assert.Single(ids);
        Assert.Equal("1", await stack.ScalarAsync("SELECT count(*) FROM bookings"));
    }

    [Fact(Timeout = 30_000)]
    public async Task Cancelling_keeps_worker_steps_written_after_the_api_read_the_row()
    {
        var (checkIn, checkOut) = Stay(320);
        var created = await stack.PostBookingAsync(checkIn, checkOut);
        var id = created.GetProperty("id").GetString()!;

        await using var dataSource = NpgsqlDataSource.Create(stack.PostgresUri);
        var repository = new BookingRepository(dataSource);
        var readByApi = (await repository.FindAsync(id, CancellationToken.None))!;

        // The worker advances the timeline after the API has read the row.
        // The status is still pending, so the cancel's WHERE clause matches.
        var now = DateTime.UtcNow;
        var delivered = BookingTimeline.Advance(readByApi.Events, BookingTimeline.Delivered, now);
        await repository.UpdateEventsAsync(id, delivered, CancellationToken.None);

        var cancelled = await repository.TryCancelAsync(
            id,
            DateOnly.FromDateTime(now),
            BookingTimeline.CancelledEvent(now),
            CancellationToken.None);

        Assert.NotNull(cancelled);
        Assert.Equal(BookingStatuses.Cancelled, cancelled.Status);
        Assert.Equal(delivered.Count + 1, cancelled.Events.Count);
        Assert.True(cancelled.Events[BookingTimeline.Delivered].Done);
        Assert.Equal(BookingLabels.Cancelled, cancelled.Events[^1].Label);
        Assert.DoesNotContain(cancelled.Events, e => e.Current);
    }

    [Fact(Timeout = 60_000)]
    public async Task A_cancel_racing_the_worker_always_ends_cancelled_and_frees_the_room()
    {
        await stack.StartWorkerAsync();
        try
        {
            for (var round = 0; round < 5; round++)
            {
                var (checkIn, checkOut) = Stay(330 + round * 10);
                var created = await stack.PostBookingAsync(checkIn, checkOut);
                var id = created.GetProperty("id").GetString()!;

                var cancel = await stack.Client.PostAsync($"/api/bookings/{id}/cancel", null);
                Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

                // Give a worker that lost the race time to try its write.
                await Task.Delay(300);
                var stored = await stack.GetBookingAsync(id);
                Assert.Equal("cancelled", stored.GetProperty("status").GetString());
                var events = stored.GetProperty("events");
                Assert.Equal(BookingLabels.Cancelled, events[events.GetArrayLength() - 1].GetProperty("label").GetString());

                var query = $"/api/hotels/h_porter/availability?checkIn={checkIn:yyyy-MM-dd}&checkOut={checkOut:yyyy-MM-dd}";
                var availability = await stack.Client.GetFromJsonAsync<JsonElement>(query);
                var room = availability.GetProperty("rooms").EnumerateArray().Single(r => r.GetProperty("id").GetString() == "r1");
                Assert.True(room.GetProperty("available").GetBoolean());
            }
        }
        finally
        {
            await stack.StopWorkerAsync();
        }
    }

    private static object Body(DateOnly checkIn, DateOnly checkOut, string guestName)
    {
        var nights = checkOut.DayNumber - checkIn.DayNumber;
        return new
        {
            hotelId = "h_porter",
            roomId = "r1",
            guestName,
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            nights,
            total = 150m * nights,
        };
    }

    private static (DateOnly CheckIn, DateOnly CheckOut) Stay(int daysAhead)
    {
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(daysAhead);
        return (checkIn, checkIn.AddDays(3));
    }
}

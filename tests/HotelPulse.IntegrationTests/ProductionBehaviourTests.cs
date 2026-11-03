using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HotelPulse.Infrastructure;
using HotelPulse.Infrastructure.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace HotelPulse.IntegrationTests;

/// <summary>ADR-0012 and ADR-0013: what the API does besides the booking rules.</summary>
[Collection("stack")]
public sealed class ProductionBehaviourTests(HotelPulseStack stack) : IAsyncLifetime
{
    public Task InitializeAsync() => stack.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Liveness_answers_without_checking_dependencies()
    {
        var body = await stack.Client.GetFromJsonAsync<JsonElement>("/health/live");
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_request_id_from_the_caller_is_returned()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/hotels");
        request.Headers.Add("X-Request-Id", "abc-12345678");

        var response = await stack.Client.SendAsync(request);

        Assert.Equal("abc-12345678", response.Headers.GetValues("X-Request-Id").Single());
    }

    [Fact]
    public async Task A_missing_or_malformed_request_id_is_replaced()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/hotels");
        request.Headers.TryAddWithoutValidation("X-Request-Id", "bad id with spaces");

        var response = await stack.Client.SendAsync(request);

        Assert.Matches("^[0-9a-f]{32}$", response.Headers.GetValues("X-Request-Id").Single());
    }

    [Fact]
    public async Task Responses_carry_security_headers_and_no_server_banner()
    {
        var response = await stack.Client.GetAsync("/api/hotels");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task The_booking_limit_counts_per_client_and_resets_each_minute()
    {
        using var redis = await ConnectionMultiplexer.ConnectAsync(stack.RedisUri);
        var limiter = Limiter(redis, limit: 2);
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(RateLimitDecision.Allowed, await limiter.CheckAsync("10.0.0.1", now));
        Assert.Equal(RateLimitDecision.Allowed, await limiter.CheckAsync("10.0.0.1", now));
        Assert.Equal(RateLimitDecision.Limited, await limiter.CheckAsync("10.0.0.1", now));
        Assert.Equal(RateLimitDecision.Allowed, await limiter.CheckAsync("10.0.0.2", now));
        Assert.Equal(RateLimitDecision.Allowed, await limiter.CheckAsync("10.0.0.1", now.AddMinutes(1)));
    }

    [Fact]
    public async Task The_booking_limit_lets_requests_through_when_redis_is_down()
    {
        var options = ConfigurationOptions.Parse("127.0.0.1:1");
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 200;
        options.SyncTimeout = 200;
        options.AsyncTimeout = 200;
        using var deadRedis = await ConnectionMultiplexer.ConnectAsync(options);

        Assert.Equal(RateLimitDecision.SkippedRedisDown, await Limiter(deadRedis, limit: 1).CheckAsync("10.0.0.3", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task A_limit_of_zero_turns_the_check_off()
    {
        using var redis = await ConnectionMultiplexer.ConnectAsync(stack.RedisUri);
        var limiter = Limiter(redis, limit: 0);

        for (var i = 0; i < 5; i++)
            Assert.Equal(RateLimitDecision.Allowed, await limiter.CheckAsync("10.0.0.4", DateTimeOffset.UtcNow));
    }

    private static BookingRateLimiter Limiter(IConnectionMultiplexer redis, int limit)
    {
        var settings = HotelPulseSettings.From(new ConfigurationBuilder().Build()) with { BookingRateLimitPerMinute = limit };
        return new BookingRateLimiter(redis, settings, NullLogger<BookingRateLimiter>.Instance);
    }
}

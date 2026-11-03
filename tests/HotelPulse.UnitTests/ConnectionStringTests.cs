using HotelPulse.Infrastructure;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace HotelPulse.UnitTests;

public class PostgresConnectionTests
{
    [Fact]
    public void A_neon_url_requires_tls_and_keeps_the_pool_small()
    {
        var connectionString = PostgresConnection.ToNpgsql(
            "postgresql://neondb_owner:p%40ss@ep-cool-pooler.eu-central-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require",
            sslModeOverride: null,
            maxPoolSize: PostgresConnection.DefaultMaxPoolSize);

        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("ep-cool-pooler.eu-central-1.aws.neon.tech", parsed.Host);
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("neondb_owner", parsed.Username);
        Assert.Equal("p@ss", parsed.Password);
        Assert.Equal("neondb", parsed.Database);
        Assert.Equal(SslMode.Require, parsed.SslMode);
        Assert.Equal(5, parsed.MaxPoolSize);
        Assert.Equal(0, parsed.MinPoolSize);
    }

    [Fact]
    public void An_explicit_ssl_override_wins()
    {
        var connectionString = PostgresConnection.ToNpgsql(
            "Host=localhost;Database=hotelpulse;Username=hotelpulse;Password=hotelpulse",
            sslModeOverride: "disable",
            maxPoolSize: 5);

        Assert.Equal(SslMode.Disable, new NpgsqlConnectionStringBuilder(connectionString).SslMode);
    }

    [Fact]
    public void A_hostname_without_sslmode_still_requires_tls()
    {
        var connectionString = PostgresConnection.ToNpgsql(
            "postgresql://user:secret@ep-example.neon.tech/app",
            sslModeOverride: null,
            maxPoolSize: 5);

        Assert.Equal(SslMode.Require, new NpgsqlConnectionStringBuilder(connectionString).SslMode);
    }

    [Theory]
    [InlineData("verify-full", SslMode.VerifyFull)]
    [InlineData("prefer", SslMode.Prefer)]
    public void Libpq_ssl_modes_map_onto_npgsql(string sslMode, SslMode expected)
    {
        Assert.Equal(expected, PostgresConnection.ParseSslMode(sslMode));
    }
}

public class RedisConnectionTests
{
    [Fact]
    public void Rediss_enables_tls_and_keeps_the_password()
    {
        var options = RedisConnection.ToOptions("rediss://default:s3cret@cache.example:6380/2");

        Assert.True(options.Ssl);
        Assert.False(options.AbortOnConnectFail);
        Assert.Equal("default", options.User);
        Assert.Equal("s3cret", options.Password);
        Assert.Equal(2, options.DefaultDatabase);
        Assert.Contains(options.EndPoints, endpoint => endpoint.ToString()!.Contains("cache.example"));
    }

    [Fact]
    public void A_plain_redis_url_stays_unencrypted()
    {
        var options = RedisConnection.ToOptions("redis://red-abc:6379");

        Assert.False(options.Ssl);
        Assert.Contains(options.EndPoints, endpoint => endpoint.ToString()!.Contains("red-abc"));
    }
}

public class HotelPulseSettingsTests
{
    [Fact]
    public void Render_urls_win_over_the_local_names()
    {
        var settings = HotelPulseSettings.From(Config(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://user:secret@ep-example.neon.tech/app?sslmode=require",
            ["POSTGRES_URI"] = "Host=localhost;Database=ignored",
            ["REDIS_URL"] = "redis://red-abc:6379",
            ["REDIS_URI"] = "localhost:6379",
            ["AMQP_URL"] = "amqps://user:secret@chimpanzee.rmq.cloudamqp.com/user",
            ["RABBITMQ_URI"] = "amqp://guest:guest@localhost:5672/",
            ["RUN_WORKER_IN_PROCESS"] = "true",
            ["HOTELPULSE_CONFIRM_PROBABILITY"] = "1",
        }));

        Assert.Contains("ep-example.neon.tech", settings.PostgresUri, StringComparison.Ordinal);
        Assert.Equal(SslMode.Require, new NpgsqlConnectionStringBuilder(settings.PostgresUri).SslMode);
        Assert.Equal("redis://red-abc:6379", settings.RedisUri);
        Assert.StartsWith("amqps://", settings.RabbitMqUri, StringComparison.Ordinal);
        Assert.True(settings.RunWorkerInProcess);
        Assert.Equal(1, settings.ConfirmProbability);
    }

    [Fact]
    public void The_worker_stays_a_separate_process_unless_asked()
    {
        var settings = HotelPulseSettings.From(Config([]));
        Assert.False(settings.RunWorkerInProcess);
        Assert.Equal(0.9, settings.ConfirmProbability);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("8080")]
    public void Port_binding_is_optional(string? port)
    {
        if (string.IsNullOrEmpty(port))
            Assert.Null(ListenAddress.ForPort(port));
        else
            Assert.Equal("http://0.0.0.0:8080", ListenAddress.ForPort(port));
    }

    [Fact]
    public void A_startup_retry_waits_longer_between_the_first_attempts()
    {
        Assert.Equal(
            [1, 2, 4, 8, 8, 8, 8],
            StartupConnections.RetryDelays.Select(delay => (int)delay.TotalSeconds).ToArray());
        Assert.True(StartupConnections.IsTransient(new TimeoutException()));
        Assert.True(StartupConnections.IsTransient(new InvalidOperationException("wrapped", new NpgsqlException("starting up"))));
        Assert.False(StartupConnections.IsTransient(new InvalidOperationException("bug")));
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}

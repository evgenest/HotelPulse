using System.Net.Sockets;
using Npgsql;

namespace HotelPulse.Infrastructure;

public static class PostgresConnection
{
    public const int DefaultMaxPoolSize = 5;

    public static string ToNpgsql(string value, string? sslModeOverride, int maxPoolSize)
    {
        if (maxPoolSize < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPoolSize), "Pool size must be at least 1.");

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            var keyword = new NpgsqlConnectionStringBuilder(value)
            {
                MaxPoolSize = maxPoolSize,
                MinPoolSize = 0,
                Timeout = 20,
            };
            if (!string.IsNullOrWhiteSpace(sslModeOverride))
                keyword.SslMode = ParseSslMode(sslModeOverride);
            return keyword.ConnectionString;
        }

        var uri = new Uri(value);
        var (user, password) = SplitUserInfo(uri.UserInfo);
        var query = ParseQuery(uri.Query);
        query.TryGetValue("sslmode", out var sslFromUri);
        var ssl = FirstNonEmpty(sslModeOverride, sslFromUri) ?? (uri.Host.Contains('.') ? "require" : "disable");

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Username = user,
            Password = password,
            Database = uri.AbsolutePath.TrimStart('/'),
            SslMode = ParseSslMode(ssl),
            MaxPoolSize = maxPoolSize,
            MinPoolSize = 0,
            Timeout = 20,
        };
        return builder.ConnectionString;
    }

    public static SslMode ParseSslMode(string value) => value.Trim().ToLowerInvariant() switch
    {
        "disable" => SslMode.Disable,
        "allow" => SslMode.Allow,
        "prefer" => SslMode.Prefer,
        "require" => SslMode.Require,
        "verify-ca" or "verifyca" => SslMode.VerifyCA,
        "verify-full" or "verifyfull" => SslMode.VerifyFull,
        _ => throw new ArgumentException($"Unsupported sslmode '{value}'. Use disable, allow, prefer, require, verify-ca, or verify-full."),
    };

    private static (string User, string Password) SplitUserInfo(string userInfo)
    {
        if (userInfo.Length == 0)
            return ("", "");

        var colon = userInfo.IndexOf(':');
        if (colon < 0)
            return (Uri.UnescapeDataString(userInfo), "");

        return (
            Uri.UnescapeDataString(userInfo[..colon]),
            Uri.UnescapeDataString(userInfo[(colon + 1)..]));
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (query.Length <= 1)
            return result;

        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0)
                continue;
            result[Uri.UnescapeDataString(part[..eq])] = Uri.UnescapeDataString(part[(eq + 1)..]);
        }

        return result;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}

public static class RedisConnection
{
    public static StackExchange.Redis.ConfigurationOptions ToOptions(string value)
    {
        if (value.StartsWith("redis://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(value);
            var tls = uri.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase);
            var options = new StackExchange.Redis.ConfigurationOptions
            {
                Ssl = tls,
                AbortOnConnectFail = false,
            };
            options.EndPoints.Add(uri.Host, uri.Port > 0 ? uri.Port : tls ? 6380 : 6379);

            if (uri.UserInfo.Length > 0)
            {
                var colon = uri.UserInfo.IndexOf(':');
                if (colon < 0)
                    options.Password = Uri.UnescapeDataString(uri.UserInfo);
                else if (colon == 0)
                    options.Password = Uri.UnescapeDataString(uri.UserInfo[1..]);
                else
                {
                    options.User = Uri.UnescapeDataString(uri.UserInfo[..colon]);
                    options.Password = Uri.UnescapeDataString(uri.UserInfo[(colon + 1)..]);
                }
            }

            var path = uri.AbsolutePath.TrimStart('/');
            if (path.Length > 0 && int.TryParse(path, out var database))
                options.DefaultDatabase = database;

            return options;
        }

        var parsed = StackExchange.Redis.ConfigurationOptions.Parse(value);
        parsed.AbortOnConnectFail = false;
        return parsed;
    }
}

public static class StartupConnections
{
    public static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(8),
    ];

    public static bool IsTransient(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException or SocketException or IOException or NpgsqlException)
                return true;
        }

        return false;
    }
}

public static class ListenAddress
{
    public static string? ForPort(string? port)
    {
        if (string.IsNullOrWhiteSpace(port))
            return null;

        if (!int.TryParse(port, out var parsed) || parsed is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "PORT must be a TCP port between 1 and 65535.");

        return $"http://0.0.0.0:{parsed}";
    }
}

public static class Flag
{
    public static bool IsEnabled(string? value) =>
        value is not null && value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
}

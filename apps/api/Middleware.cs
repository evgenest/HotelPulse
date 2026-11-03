using System.Text.RegularExpressions;

/// <summary>
/// Production middleware, same behaviour as the Laravel API (ADR-0012, ADR-0013):
/// X-Request-Id on every response and in every log line of the request,
/// security headers, and a bare 500 body.
/// </summary>
public static partial class HotelPulseMiddleware
{
    [GeneratedRegex("^[A-Za-z0-9._-]{8,128}$")]
    private static partial Regex RequestIdFormat();

    public static void Use(WebApplication app)
    {
        // Unexpected errors: log them, answer with no details.
        app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "Internal server error." });
        }));

        app.Use(async (context, next) =>
        {
            var incoming = context.Request.Headers["X-Request-Id"].ToString();
            var id = RequestIdFormat().IsMatch(incoming) ? incoming : Guid.NewGuid().ToString("N");
            context.TraceIdentifier = id;
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["X-Request-Id"] = id;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["Referrer-Policy"] = "no-referrer";
                return Task.CompletedTask;
            });

            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("HotelPulse.Request");
            using (logger.BeginScope(new Dictionary<string, object> { ["requestId"] = id }))
                await next(context);
        });
    }
}

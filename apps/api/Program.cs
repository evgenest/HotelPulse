using System.Text.Json;
using System.Text.Json.Serialization;
using HotelPulse.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// One JSON object per line, with the request id from the scope (ADR-0012).
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

var listen = ListenAddress.ForPort(builder.Configuration["PORT"]);
if (listen is not null)
    builder.WebHost.UseUrls(listen);

builder.Services.ConfigureHttpJsonOptions(opt =>
{
    opt.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    opt.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var corsOrigins = builder.Configuration["CORS_ORIGINS"];
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader().AllowAnyMethod();
        if (string.IsNullOrWhiteSpace(corsOrigins) || corsOrigins.Trim() == "*")
            policy.AllowAnyOrigin();
        else
            policy.WithOrigins(corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }));

builder.Services.AddHotelPulse(builder.Configuration, HotelPulseRole.Api);

var app = builder.Build();
HotelPulseMiddleware.Use(app);
app.UseCors();
HotelPulseEndpoints.Map(app);
app.Run();

using System.Text.Json;
using System.Text.Json.Serialization;
using HotelPulse.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

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
app.UseCors();
HotelPulseEndpoints.Map(app);
app.Run();

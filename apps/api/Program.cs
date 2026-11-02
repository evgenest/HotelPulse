using System.Text.Json;
using System.Text.Json.Serialization;
using HotelPulse.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(opt =>
{
    opt.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    opt.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddHotelPulse(builder.Configuration, HotelPulseRole.Api);

var app = builder.Build();
app.UseCors();
HotelPulseEndpoints.Map(app);
app.Run();

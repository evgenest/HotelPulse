using HotelPulse.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});
builder.Services.AddHotelPulse(builder.Configuration, HotelPulseRole.Worker);
await builder.Build().RunAsync();

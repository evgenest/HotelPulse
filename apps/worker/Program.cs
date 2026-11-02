using HotelPulse.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHotelPulse(builder.Configuration, HotelPulseRole.Worker);
await builder.Build().RunAsync();

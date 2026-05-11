using HotelPulse.Worker;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(cfg =>
    {
        cfg.AddEnvironmentVariables();
    })
    .ConfigureServices(services =>
    {
        services.AddHostedService<BookingConsumer>();
    })
    .Build();

await host.RunAsync();

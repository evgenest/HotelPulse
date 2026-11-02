using Dapper;
using HotelPulse.Domain;
using HotelPulse.Infrastructure.Caching;
using HotelPulse.Infrastructure.Messaging;
using HotelPulse.Infrastructure.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StackExchange.Redis;

namespace HotelPulse.Infrastructure;

public enum HotelPulseRole
{
    Api,
    Worker,
}

public static class DependencyInjection
{
    public static IServiceCollection AddHotelPulse(
        this IServiceCollection services,
        IConfiguration configuration,
        HotelPulseRole role)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var settings = HotelPulseSettings.From(configuration);
        services.AddSingleton(settings);
        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(settings.PostgresUri).Build());
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(settings.RedisUri);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });

        services.AddSingleton<DatabaseMigrator>();
        services.AddHostedService<MigrateOnStartup>();
        services.AddSingleton<HotelRepository>();
        services.AddSingleton<BookingRepository>();
        services.AddSingleton<IBookingStore>(sp => sp.GetRequiredService<BookingRepository>());
        services.AddSingleton<IBookingCommands>(sp => sp.GetRequiredService<BookingRepository>());
        services.AddSingleton<RedisHotelCache>();
        services.AddSingleton<HotelCatalog>();
        services.AddSingleton<BookingPublisher>();
        services.AddSingleton<IBookingOutcomePolicy>(new RandomBookingOutcomePolicy(settings.ConfirmProbability));
        services.AddSingleton(sp => new BookingProcessor(
            sp.GetRequiredService<IBookingStore>(),
            sp.GetRequiredService<IBookingOutcomePolicy>(),
            settings.StepDelayMs,
            () => DateTime.UtcNow));

        if (role == HotelPulseRole.Api)
            services.AddHostedService<OutboxRelay>();

        if (role == HotelPulseRole.Worker)
            services.AddHostedService<BookingConsumer>();

        return services;
    }
}

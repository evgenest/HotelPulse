using Microsoft.Extensions.Hosting;

namespace HotelPulse.Infrastructure.Postgres;

public sealed class MigrateOnStartup(DatabaseMigrator migrator) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => migrator.ApplyAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

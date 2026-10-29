using DataTrustHub.Features._Shared.Seeding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DataTrustHub.Features.Tests.Administration.Helpers;

// The admin seeding runs in the background after the host has started (#146), so a test that checks its
// outcome first waits for the seed to finish
internal static class SeederRuns
{
    // Generous for a seed on fakes or EF InMemory; a hang fails the test instead of blocking the run
    internal static readonly TimeSpan SeedTimeout = TimeSpan.FromSeconds(30);

    internal static async Task RunToCompletionAsync(this AdminSeeder seeder)
    {
        await seeder.StartAsync(CancellationToken.None);
        await (seeder.ExecuteTask ?? Task.CompletedTask).WaitAsync(SeedTimeout);
    }

    // The seeder the host started; hosted services are singletons, so this is the running instance
    internal static AdminSeeder StartupSeeder(IServiceProvider services) =>
        services.GetServices<IHostedService>().OfType<AdminSeeder>().Single();

    internal static Task WaitForStartupSeedAsync(IServiceProvider services) =>
        (StartupSeeder(services).ExecuteTask ?? Task.CompletedTask).WaitAsync(SeedTimeout);
}

using System.Net;
using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Seeding;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Health.Helpers;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

// #146 AC5: admin seeding at startup survives a database that is unreachable after the retries. It logs
// an error without the admin password, the application keeps running, and the next start tries again.
public class AdminSeedUnreachableDatabaseTests
{
    private const string AdminEmail = "seeded-admin@example.com";
    private const string AdminPassword = "SeededPassword1!";
    private const int AlwaysUnreachable = int.MaxValue;
    private const int UnreachableOnce = 1;

    [Fact]
    public async Task Seeder_DatabaseUnreachableOnRead_DoesNotThrow()
    {
        var fakes = new SeederFakes(AlwaysUnreachable);

        var exception = await Record.ExceptionAsync(() => fakes.StartSeederAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task Seeder_DatabaseUnreachableOnRead_LogsAnError()
    {
        var fakes = new SeederFakes(AlwaysUnreachable);

        await fakes.StartSeederAsync();

        Assert.Contains(fakes.Logger.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Seeder_DatabaseUnreachableOnSave_DoesNotThrowAndLogsAnError()
    {
        var fakes = new SeederFakes(unreachableReads: 0, new UnreachableUnitOfWork());

        var exception = await Record.ExceptionAsync(() => fakes.StartSeederAsync());

        Assert.Null(exception);
        Assert.Contains(fakes.Logger.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Seeder_DatabaseUnreachable_LogsNoAdminPassword()
    {
        var fakes = new SeederFakes(AlwaysUnreachable);

        await fakes.StartSeederAsync();

        Assert.All(fakes.Logger.Entries, entry =>
        {
            Assert.DoesNotContain(AdminPassword, entry.Message);
            Assert.DoesNotContain(AdminPassword, entry.Exception?.ToString() ?? string.Empty);
        });
    }

    [Fact]
    public async Task Seeder_DatabaseUnreachable_StagesAndCommitsNothing()
    {
        var fakes = new SeederFakes(AlwaysUnreachable);

        await fakes.StartSeederAsync();

        Assert.Empty(fakes.Users.Added);
        Assert.Equal(0, fakes.CountingUnitOfWork.SaveChangesCalls);
    }

    // A new seeder, as at the next start of the application, once the database has resumed
    [Fact]
    public async Task Seeder_NextStartAfterDatabaseResumed_CreatesAdminAndCommitsOnce()
    {
        var fakes = new SeederFakes(UnreachableOnce);
        await fakes.StartSeederAsync();

        await fakes.StartSeederAsync();

        var added = Assert.Single(fakes.Users.Added);
        Assert.Equal((AdminEmail, UserRoles.Admin), (added.Email, added.Role));
        Assert.Equal(1, fakes.CountingUnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Startup_AdminSeedConfiguredAndDatabaseUnreachable_ApiKeepsRunning()
    {
        await using var factory = new SeededNoDatabaseApiFactory();

        var client = factory.CreateClient();
        await SeederRuns.WaitForStartupSeedAsync(factory.Services);

        var response = await client.GetAsync(HealthProbe.Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(factory.DatabaseTouches.Count > 0, "The seeder never reached the database.");
    }

    // The seeder resolves the repository and unit of work from its own scope, so the fakes are served by
    // a small container; singletons make every scope, and every seeder, see the same instances
    private sealed class SeederFakes
    {
        private readonly IServiceProvider provider;

        internal SeederFakes(int unreachableReads, IUnitOfWork? unitOfWork = null)
        {
            provider = new ServiceCollection()
                .AddSingleton<IUserRepository>(new ResumingUserRepository(Users, unreachableReads))
                .AddSingleton(unitOfWork ?? CountingUnitOfWork)
                .BuildServiceProvider();
        }

        internal FakeUserRepository Users { get; } = new();
        internal CountingUnitOfWork CountingUnitOfWork { get; } = new();
        internal CapturingLogger<AdminSeeder> Logger { get; } = new();

        // Runs the seed to its end, as the host does in the background
        internal Task StartSeederAsync() =>
            new AdminSeeder(
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    new FakePasswordHasher(),
                    Options.Create(new AdminSeedOptions { Email = AdminEmail, Password = AdminPassword }),
                    Logger)
                .RunToCompletionAsync();
    }

    // The real API host with an unreachable database and the admin seed configured
    private sealed class SeededNoDatabaseApiFactory : NoDatabaseApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            OverrideSettings(builder, new Dictionary<string, string?>
            {
                [$"{AdminSeedOptions.SectionName}:Email"] = AdminEmail,
                [$"{AdminSeedOptions.SectionName}:Password"] = AdminPassword
            });
        }
    }
}

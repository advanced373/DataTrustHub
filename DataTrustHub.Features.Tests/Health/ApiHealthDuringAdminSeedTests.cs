using System.Net;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Seeding;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Health.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DataTrustHub.Features.Tests.Health;

// #146 AC1 and AC5: the admin seeding is off the startup path. While the seed still waits for the database
// (a paused database that is resuming), the API host has started and answers GET /health.
public class ApiHealthDuringAdminSeedTests
{
    private const string AdminEmail = "seeded-admin@example.com";
    private const string AdminPassword = "SeededPassword1!";

    // A seed on the startup path would block the host forever; this turns that hang into a failure
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Health_AdminSeedStillWaitingForDatabase_Returns200()
    {
        await using var factory = new BlockedSeedApiFactory();
        var client = await Task.Run(factory.CreateClient).WaitAsync(StartupTimeout);
        await factory.Users.SeedReached.WaitAsync(StartupTimeout);

        var response = await client.GetAsync(HealthProbe.Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(SeederRuns.StartupSeeder(factory.Services).ExecuteTask!.IsCompleted,
            "The admin seed was expected to still be waiting for the database.");
    }

    // The API with the admin seed configured and a user repository whose read never completes
    private sealed class BlockedSeedApiFactory : ApiFactory
    {
        internal NeverAnsweringUserRepository Users { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            OverrideSettings(builder, new Dictionary<string, string?>
            {
                [$"{AdminSeedOptions.SectionName}:Email"] = AdminEmail,
                [$"{AdminSeedOptions.SectionName}:Password"] = AdminPassword
            });
            builder.ConfigureServices(services =>
                services.Replace(ServiceDescriptor.Scoped<IUserRepository>(_ => Users)));
        }
    }

    // Signals when the seed reached it, then waits until the host stops, as a read on a resuming database
    internal sealed class NeverAnsweringUserRepository : IUserRepository
    {
        private readonly TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task SeedReached => reached.Task;

        public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
        {
            reached.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }

        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw Unexpected();

        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) => throw Unexpected();

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) => throw Unexpected();

        public void Add(User user) => throw Unexpected();

        public Task UpdateAsync(User user, CancellationToken cancellationToken) => throw Unexpected();

        public Task RemoveAsync(User user, CancellationToken cancellationToken) => throw Unexpected();

        private static InvalidOperationException Unexpected() =>
            new("Only the admin seed's read by email is expected in this test.");
    }
}

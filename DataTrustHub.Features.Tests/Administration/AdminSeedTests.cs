using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Seeding;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

public class AdminSeedTests(AdminSeedTests.SeededApiFactory factory) : IClassFixture<AdminSeedTests.SeededApiFactory>
{
    private const string AdminEmail = "seeded-admin@example.com";
    private const string VariantAdminEmail = " Seeded-Admin@EXAMPLE.com ";
    private const string AdminPassword = "SeededPassword1!";
    private const string LoginEndpoint = "/auth/login";

    [Fact]
    public async Task Startup_WithAdminSeedConfigured_CreatesAdminWhoCanSignIn()
    {
        await SeederRuns.WaitForStartupSeedAsync(factory.Services);

        var response = await factory.CreateClient().PostAsJsonAsync(
            LoginEndpoint, new { Email = AdminEmail, Password = AdminPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(UserRoles.Admin, await ReadRoleAsync(AdminEmail));
    }

    [Fact]
    public async Task Seeder_ExistingUserWithoutAdminRole_IsPromotedToAdmin()
    {
        await SeederRuns.WaitForStartupSeedAsync(factory.Services);
        await SetRoleAsync(AdminEmail, UserRoles.User);

        await SeederRuns.StartupSeeder(factory.Services).RunToCompletionAsync();

        Assert.Equal(UserRoles.Admin, await ReadRoleAsync(AdminEmail));
    }

    [Fact]
    public async Task Startup_WithoutAdminSeed_CreatesNoUsers()
    {
        using var unseededFactory = new ApiFactory();
        unseededFactory.CreateClient();
        await SeederRuns.WaitForStartupSeedAsync(unseededFactory.Services);

        using var scope = unseededFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        Assert.False(await db.Users.AnyAsync());
    }

    // --- Seeder on fakes (ADR-0012 step 3c): it commits once, only when it created or promoted ---

    [Fact]
    public async Task Seeder_UnknownEmail_AddsAdminWithHashedPasswordAndCommitsOnce()
    {
        var fakes = new SeederFakes();

        await fakes.CreateSeeder(AdminEmail, AdminPassword).RunToCompletionAsync();

        var added = Assert.Single(fakes.Users.Added);
        Assert.Equal((AdminEmail, UserRoles.Admin), (added.Email, added.Role));
        Assert.NotEqual(AdminPassword, added.HashedPassword);
        Assert.True(fakes.Hasher.VerifyHashedPassword(added.HashedPassword, AdminPassword));
        Assert.Equal(1, fakes.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Seeder_ExistingNonAdmin_PromotesWithoutChangingPasswordAndCommitsOnce()
    {
        var fakes = new SeederFakes();
        var user = fakes.Users.Seed(AdminEmail);
        var originalHash = user.HashedPassword;

        await fakes.CreateSeeder(AdminEmail, AdminPassword).RunToCompletionAsync();

        Assert.Same(user, Assert.Single(fakes.Users.Updated));
        Assert.Equal((UserRoles.Admin, originalHash), (user.Role, user.HashedPassword));
        Assert.Empty(fakes.Users.Added);
        Assert.Equal(1, fakes.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Seeder_ExistingAdmin_ChangesNothingAndDoesNotCommit()
    {
        var fakes = new SeederFakes();
        var admin = fakes.Users.SeedAdmin(AdminEmail);
        var originalHash = admin.HashedPassword;

        await fakes.CreateSeeder(AdminEmail, AdminPassword).RunToCompletionAsync();

        Assert.Equal((UserRoles.Admin, originalHash), (admin.Role, admin.HashedPassword));
        Assert.Empty(fakes.Users.Added);
        Assert.Empty(fakes.Users.Updated);
        Assert.Equal(0, fakes.UnitOfWork.SaveChangesCalls);
    }

    // AC6 of #123: the configured email is a case and space variant of the stored, normalized one (U7)
    [Fact]
    public async Task Seeder_UnknownVariantEmail_AddsAdminWithTheNormalizedEmail()
    {
        var fakes = new SeederFakes();

        await fakes.CreateSeeder(VariantAdminEmail, AdminPassword).RunToCompletionAsync();

        Assert.Equal(AdminEmail, Assert.Single(fakes.Users.Added).Email);
    }

    [Fact]
    public async Task Seeder_VariantOfAStoredNonAdminsEmail_PromotesThatUserAndAddsNone()
    {
        var fakes = new SeederFakes();
        var user = fakes.Users.Seed(AdminEmail);

        await fakes.CreateSeeder(VariantAdminEmail, AdminPassword).RunToCompletionAsync();

        Assert.Same(user, Assert.Single(fakes.Users.Updated));
        Assert.Equal(UserRoles.Admin, user.Role);
        Assert.Empty(fakes.Users.Added);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(AdminEmail, "")]
    [InlineData(" ", AdminPassword)]
    public async Task Seeder_NotConfigured_CreatesNoUserAndDoesNotCommit(string? email, string? password)
    {
        var fakes = new SeederFakes();

        await fakes.CreateSeeder(email, password).RunToCompletionAsync();

        Assert.Empty(fakes.Users.Added);
        Assert.Empty(fakes.Users.Updated);
        Assert.Equal(0, fakes.UnitOfWork.SaveChangesCalls);
    }

    private async Task<string> ReadRoleAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        return (await db.Users.SingleAsync(u => u.Email == email)).Role;
    }

    private async Task SetRoleAsync(string email, string role)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        (await db.Users.SingleAsync(u => u.Email == email)).Role = role;
        await db.SaveChangesAsync();
    }

    // The seeder resolves the repository and unit of work from its own scope, so the fakes are
    // served by a small container; singletons make every scope see the same instances
    private sealed class SeederFakes
    {
        internal FakeUserRepository Users { get; } = new();
        internal CountingUnitOfWork UnitOfWork { get; } = new();
        internal FakePasswordHasher Hasher { get; } = new();

        internal AdminSeeder CreateSeeder(string? email, string? password)
        {
            var provider = new ServiceCollection()
                .AddSingleton<IUserRepository>(Users)
                .AddSingleton<IUnitOfWork>(UnitOfWork)
                .BuildServiceProvider();

            return new AdminSeeder(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Hasher,
                Options.Create(new AdminSeedOptions { Email = email, Password = password }),
                NullLogger<AdminSeeder>.Instance);
        }
    }

    public class SeededApiFactory : ApiFactory
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

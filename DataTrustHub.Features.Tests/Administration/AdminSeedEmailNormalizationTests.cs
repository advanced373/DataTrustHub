using DataTrustHub.Features._Shared.Seeding;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

// AC6 of #123: the admin seed is configured with a case and space variant of an address; the seeder creates the
// admin with the normalized address (U7), and later finds and promotes the user stored under it instead of
// creating a second one
public class AdminSeedEmailNormalizationTests(AdminSeedEmailNormalizationTests.VariantSeededApiFactory factory)
    : IClassFixture<AdminSeedEmailNormalizationTests.VariantSeededApiFactory>
{
    private const string ConfiguredEmail = " Seeded.Variant@Example.COM ";
    private const string NormalizedEmail = "seeded.variant@example.com";
    private const string AdminPassword = "SeededPassword1!";

    [Fact]
    public async Task Startup_SeedEmailWithCaseAndSpaces_CreatesOneAdminWithTheNormalizedEmail()
    {
        await SeederRuns.WaitForStartupSeedAsync(factory.Services);

        var users = await StoredUsersAsync(NormalizedEmail);

        Assert.Equal(UserRoles.Admin, Assert.Single(users).Role);
    }

    [Fact]
    public async Task Seeder_StoredUserUnderTheNormalizedEmail_IsPromotedNotDuplicated()
    {
        await SeederRuns.WaitForStartupSeedAsync(factory.Services);
        await SetRoleAsync(NormalizedEmail, UserRoles.User);

        await SeederRuns.StartupSeeder(factory.Services).RunToCompletionAsync();

        Assert.Equal(UserRoles.Admin, Assert.Single(await StoredUsersAsync(NormalizedEmail)).Role);
    }

    private async Task<List<(string Email, string Role)>> StoredUsersAsync(string storedEmail)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        var users = await db.Users.Where(u => u.Email == storedEmail).ToListAsync();
        return users.Select(u => (u.Email, u.Role)).ToList();
    }

    private async Task SetRoleAsync(string storedEmail, string role)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        (await db.Users.SingleAsync(u => u.Email == storedEmail)).Role = role;
        await db.SaveChangesAsync();
    }

    public class VariantSeededApiFactory : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            OverrideSettings(builder, new Dictionary<string, string?>
            {
                [$"{AdminSeedOptions.SectionName}:Email"] = ConfiguredEmail,
                [$"{AdminSeedOptions.SectionName}:Password"] = AdminPassword
            });
        }
    }
}

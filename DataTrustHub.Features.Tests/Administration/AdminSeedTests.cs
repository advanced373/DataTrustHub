using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features._Shared.Seeding;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

public class AdminSeedTests(AdminSeedTests.SeededApiFactory factory) : IClassFixture<AdminSeedTests.SeededApiFactory>
{
    private const string AdminEmail = "seeded-admin@example.com";
    private const string AdminPassword = "SeededPassword1!";
    private const string LoginEndpoint = "/auth/login";

    [Fact]
    public async Task Startup_WithAdminSeedConfigured_CreatesAdminWhoCanSignIn()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            LoginEndpoint, new { Email = AdminEmail, Password = AdminPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(UserRoles.Admin, await ReadRoleAsync(AdminEmail));
    }

    [Fact]
    public async Task Seeder_ExistingUserWithoutAdminRole_IsPromotedToAdmin()
    {
        await SetRoleAsync(AdminEmail, UserRoles.User);

        await GetSeeder(factory.Services).StartAsync(CancellationToken.None);

        Assert.Equal(UserRoles.Admin, await ReadRoleAsync(AdminEmail));
    }

    [Fact]
    public async Task Startup_WithoutAdminSeed_CreatesNoUsers()
    {
        using var unseededFactory = new ApiFactory();
        unseededFactory.CreateClient();

        using var scope = unseededFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        Assert.False(await db.Users.AnyAsync());
    }

    private static AdminSeeder GetSeeder(IServiceProvider services) =>
        services.GetServices<IHostedService>().OfType<AdminSeeder>().Single();

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

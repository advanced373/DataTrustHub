using DataTrustHub.Infrastructure.Persistance;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataTrustHub.Features.Tests.Helpers;

public class ApiFactory : WebApplicationFactory<Program>
{
    // The host's real password hasher (#167), with fewer PBKDF2 iterations than Identity's default (100 000) so
    // the many users the suite signs in do not slow it down. The hasher's own tests use the default
    internal const int TestPasswordHashIterationCount = 1_000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Tests must not pick up the developer's User Secrets (e.g. a seeded admin)
        OverrideSettings(builder, new Dictionary<string, string?>
        {
            ["AdminSeed:Email"] = string.Empty,
            ["AdminSeed:Password"] = string.Empty
        });
        TestJwt.SupplySigningKey(builder);

        builder.ConfigureServices(services =>
        {
            RemoveDbContext(services);

            var dbName = Guid.NewGuid().ToString();
            services.AddDbContext<DContext>(options =>
                options.UseInMemoryDatabase(dbName));

            services.Configure<PasswordHasherOptions>(options => options.IterationCount = TestPasswordHashIterationCount);
        });
    }

    // Removes all DbContext-related registrations, so a test provider can be added without a dual-provider conflict
    protected static void RemoveDbContext(IServiceCollection services)
    {
        var descriptorsToRemove = services
            .Where(d =>
                d.ServiceType == typeof(DbContextOptions<DContext>) ||
                d.ServiceType == typeof(DbContextOptions) ||
                d.ServiceType == typeof(DContext) ||
                d.ServiceType == typeof(IDbContextOptionsConfiguration<DContext>))
            .ToList();

        foreach (var descriptor in descriptorsToRemove)
            services.Remove(descriptor);
    }

    // Added last, so these values win over appsettings, User Secrets and environment variables
    protected static void OverrideSettings(IWebHostBuilder builder, IDictionary<string, string?> settings) =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
}

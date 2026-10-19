using DataTrustHub.Infrastructure.Persistance;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataTrustHub.Features.Tests.Helpers;

public class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Tests must not pick up the developer's User Secrets (e.g. a seeded admin)
        OverrideSettings(builder, new Dictionary<string, string?>
        {
            ["AdminSeed:Email"] = string.Empty,
            ["AdminSeed:Password"] = string.Empty
        });

        builder.ConfigureServices(services =>
        {
            // Remove all DbContext-related registrations to avoid dual-provider conflict
            var descriptorsToRemove = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<DContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(DContext) ||
                    d.ServiceType == typeof(IDbContextOptionsConfiguration<DContext>))
                .ToList();

            foreach (var descriptor in descriptorsToRemove)
                services.Remove(descriptor);

            var dbName = Guid.NewGuid().ToString();
            services.AddDbContext<DContext>(options =>
                options.UseInMemoryDatabase(dbName));
        });
    }

    // Added last, so these values win over appsettings, User Secrets and environment variables
    protected static void OverrideSettings(IWebHostBuilder builder, IDictionary<string, string?> settings) =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
}

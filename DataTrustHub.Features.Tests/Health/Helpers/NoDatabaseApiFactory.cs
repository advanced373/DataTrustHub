using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DataTrustHub.Features.Tests.Health.Helpers;

// The API with an unreachable database: resolving DContext or its options counts the touch and throws,
// as opening a connection to a paused database would fail
public class NoDatabaseApiFactory : ApiFactory
{
    private const string UnreachableMessage = "The database is unreachable in this test.";

    internal TouchCounter DatabaseTouches { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            RemoveDbContext(services);
            services.AddScoped<DContext>(_ => Unreachable<DContext>());
            services.AddScoped<DbContextOptions<DContext>>(_ => Unreachable<DbContextOptions<DContext>>());
        });
    }

    private T Unreachable<T>()
    {
        DatabaseTouches.Touch();
        throw new InvalidOperationException(UnreachableMessage);
    }
}

using DataTrustHub.Infrastructure;
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataTrustHub.Features.Tests.Helpers;

// The production read-side registrations (AddInfrastructure, ADR-0012 step 4) over a context the test owns, so a
// handler test gets the real I*Queries adapters without seeing their internal types
internal static class ReadSide
{
    internal static ServiceProvider Over(DContext db)
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().Build());
        // The last registration wins: the adapters read the test's context, which the provider does not dispose
        services.AddSingleton(db);
        return services.BuildServiceProvider();
    }
}

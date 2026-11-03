using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure.Helpers;

// The real wiring on a throwaway SQL Server LocalDB database built by the migrations, so the API runs against the
// schema production gets. Where LocalDB is missing nothing is created; the tests using it are LocalDbFact and skip.
public sealed class LocalDbApiFactory : ApiFactory, IAsyncLifetime
{
    public string Database { get; } = LocalDb.NewDatabaseName();

    async Task IAsyncLifetime.InitializeAsync()
    {
        if (!LocalDb.IsAvailable) return;
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        try
        {
            await DisposeAsync();
        }
        finally
        {
            if (LocalDb.IsAvailable) LocalDb.Drop(Database);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            RemoveDbContext(services);
            services.AddDbContext<DContext>(options => options.UseSqlServer(LocalDb.ConnectionString(Database)));
        });
    }
}

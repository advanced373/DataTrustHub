using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure.Helpers;

// The real wiring (repositories, unit of work) on an in-memory SQLite database instead of EF InMemory.
// SQLite is relational, so unique indexes and SQL translation are enforced like in SQL Server.
public sealed class SqliteApiFactory : ApiFactory, IAsyncLifetime
{
    private const string ConnectionString = "DataSource=:memory:";

    // One open connection keeps the in-memory database alive for the whole fixture
    private readonly SqliteConnection connection = new(ConnectionString);

    async Task IAsyncLifetime.InitializeAsync()
    {
        await connection.OpenAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DContext>().Database.EnsureCreatedAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await connection.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            RemoveDbContext(services);
            services.AddDbContext<DContext>(options => options.UseSqlite(connection));
        });
    }
}

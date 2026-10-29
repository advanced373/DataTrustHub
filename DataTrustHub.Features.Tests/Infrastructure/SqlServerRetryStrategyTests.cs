using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using DataTrustHub.Infrastructure;
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #146 AC4: every SQL Server context registered by the infrastructure retries transient failures long
// enough to outlast a resume of the paused serverless database (ADR-0021). AC6: the test providers do not.
// No connection is opened: building a context and its execution strategy does not touch the server.
public class SqlServerRetryStrategyTests
{
    private const string ConnectionStringKey = "ConnectionStrings:DefaultConnection";
    private const string UnreachableConnectionString = "Server=sql.invalid;Database=DataTrustHub;Encrypt=True";

    // The free-offer serverless database resumes in up to about a minute (spec #146)
    private static readonly TimeSpan MinimumResumeCovered = TimeSpan.FromSeconds(60);

    [Fact]
    public void AddInfrastructure_RegistersAtLeastOneDbContext()
    {
        Assert.NotEmpty(RegisteredContextTypes(BuildInfrastructure()));
    }

    [Fact]
    public void AddInfrastructure_EveryDbContext_UsesSqlServerRetryingStrategy()
    {
        var services = BuildInfrastructure();
        using var provider = services.BuildServiceProvider();

        foreach (var contextType in RegisteredContextTypes(services))
            Assert.IsType<SqlServerRetryingExecutionStrategy>(CreateStrategy(provider, contextType));
    }

    [Fact]
    public void AddInfrastructure_DContextRetries_UseTheNamedRetryPolicy()
    {
        var probe = CreateProbe(BuildInfrastructure().BuildServiceProvider());

        Assert.Equal(SqlServerRetryPolicy.MaxRetryCount, probe.RetryCount);
        Assert.Equal(SqlServerRetryPolicy.MaxRetryDelay, probe.RetryDelayCap);
    }

    [Fact]
    public void AddInfrastructure_DContextRetries_WaitAtLeastOneMinuteInTotal()
    {
        var probe = CreateProbe(BuildInfrastructure().BuildServiceProvider());

        Assert.True(probe.TotalDelay() >= MinimumResumeCovered,
            $"The retries wait {probe.TotalDelay()} in total, less than {MinimumResumeCovered}.");
    }

    // The audit log builds its own context from the options of DContext (ADR-0016), so it retries too
    [Fact]
    public void AddInfrastructure_AuditLogContextOptions_UseSqlServerRetryingStrategy()
    {
        using var provider = BuildInfrastructure().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<DContext>>();
        using var auditContext = new DContext(options);

        Assert.IsType<SqlServerRetryingExecutionStrategy>(auditContext.Database.CreateExecutionStrategy());
    }

    [Fact]
    public void TestHost_InMemoryDatabase_DoesNotUseSqlServerRetries()
    {
        using var factory = new ApiFactory();

        Assert.IsNotType<SqlServerRetryingExecutionStrategy>(CreateStrategy(factory.Services, typeof(DContext)));
    }

    [Fact]
    public async Task TestHost_SqliteDatabase_DoesNotUseSqlServerRetries()
    {
        await using var factory = new SqliteApiFactory();

        Assert.IsNotType<SqlServerRetryingExecutionStrategy>(CreateStrategy(factory.Services, typeof(DContext)));
    }

    private static IServiceCollection BuildInfrastructure()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ConnectionStringKey] = UnreachableConnectionString })
            .Build();
        return new ServiceCollection().AddInfrastructure(configuration);
    }

    // Every closed DbContextOptions<TContext> the infrastructure registers names one context type
    private static IReadOnlyList<Type> RegisteredContextTypes(IServiceCollection services) =>
        services
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DbContextOptions<>))
            .Select(type => type.GetGenericArguments()[0])
            .Distinct()
            .ToList();

    private static IExecutionStrategy CreateStrategy(IServiceProvider provider, Type contextType)
    {
        using var scope = provider.CreateScope();
        var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
        return context.Database.CreateExecutionStrategy();
    }

    private static RetryProbe CreateProbe(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DContext>();
        var configured = Assert.IsType<SqlServerRetryingExecutionStrategy>(context.Database.CreateExecutionStrategy());
        return new RetryProbe(context, configured);
    }

    // Reads the settings of the configured strategy and replays its back-off with a strategy of the same
    // settings, asking it for the delay before each retry until it gives up
    private sealed class RetryProbe(DbContext context, SqlServerRetryingExecutionStrategy configured)
        : SqlServerRetryingExecutionStrategy(context, configured.MaxRetryCount, configured.MaxRetryDelay, null)
    {
        private const string TransientFailure = "Database is not currently available (error 40613).";

        internal int RetryCount => MaxRetryCount;

        internal TimeSpan RetryDelayCap => MaxRetryDelay;

        internal TimeSpan TotalDelay()
        {
            ExceptionsEncountered.Clear();
            var total = TimeSpan.Zero;
            var failure = new TimeoutException(TransientFailure);
            while (true)
            {
                ExceptionsEncountered.Add(failure);
                if (GetNextDelay(failure) is not { } delay) return total;
                total += delay;
            }
        }
    }
}

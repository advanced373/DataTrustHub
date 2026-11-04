using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DataTrustHub.Features.Tests.Administration.Helpers;

// The API on EF InMemory, whose unit of work can be told that a concurrent request saved the same values
// first: the next save is then rejected as the unique index rejects it on SQL Server (#121). EF InMemory
// enforces no unique index, so this is how a test reaches the use case's handling of that rejection.
public sealed class ConcurrentSaveApiFactory : ApiFactory
{
    internal ConcurrentSave ConcurrentSave { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddSingleton(ConcurrentSave);
            services.AddScoped<IUnitOfWork, ConcurrentSaveUnitOfWork>();
        });
    }
}

// Armed by a test right before the request it wants rejected; one save is rejected, then saves go through
internal sealed class ConcurrentSave
{
    private int armed;

    internal int RejectedSaves { get; private set; }

    internal void SavedFirstByAnotherRequest() => Interlocked.Exchange(ref armed, 1);

    internal bool TakeRejection()
    {
        if (Interlocked.Exchange(ref armed, 0) == 0) return false;
        RejectedSaves++;
        return true;
    }
}

internal sealed class ConcurrentSaveUnitOfWork(DContext db, ConcurrentSave concurrentSave) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        concurrentSave.TakeRejection()
            ? throw new UniqueConstraintViolationException(new InvalidOperationException())
            : db.SaveChangesAsync(cancellationToken);
}

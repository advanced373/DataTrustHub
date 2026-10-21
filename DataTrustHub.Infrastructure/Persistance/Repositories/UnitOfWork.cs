using DataTrustHub.Domain.Abstractions;

namespace DataTrustHub.Infrastructure.Persistance.Repositories;

// The repositories and this unit of work share the scoped DContext, so one call commits a whole command
internal sealed class UnitOfWork(DContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

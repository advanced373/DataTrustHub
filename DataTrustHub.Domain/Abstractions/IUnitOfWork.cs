namespace DataTrustHub.Domain.Abstractions;

// Commits every change the repositories collected during one command, in a single transaction.
// The signature matches DbContext.SaveChangesAsync, so the EF context implements it directly.
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

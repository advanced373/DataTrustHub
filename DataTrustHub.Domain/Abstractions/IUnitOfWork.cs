namespace DataTrustHub.Domain.Abstractions;

// Commits every change the repositories collected during one command, in a single transaction.
// The signature matches DbContext.SaveChangesAsync, so the EF context implements it directly.
// A save that breaks a unique index throws UniqueConstraintViolationException.
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

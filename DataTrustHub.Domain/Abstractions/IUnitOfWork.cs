namespace DataTrustHub.Domain.Abstractions;

// Commits every change the repositories collected during one command, in a single transaction
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

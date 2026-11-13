namespace DataTrustHub.Domain.Shares;

// Every write is tracked and committed by IUnitOfWork; the repository never saves on its own
public interface IShareRepository
{
    Task<Share?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    // Invariant S3: whether the recipient already holds a share of the item that is active at now
    // (Share.IsActiveAt, in the database)
    Task<bool> HasActiveAsync(Guid dataItemId, Guid recipientUserId, DateTime now, CancellationToken cancellationToken);

    void Add(Share share);

    // Persists a revocation
    Task UpdateAsync(Share share, CancellationToken cancellationToken);

    // Deleting a data item ends all its shares; a share already revoked keeps its revocation time
    Task RevokeAllForDataItemAsync(Guid dataItemId, DateTime revokedAt, CancellationToken cancellationToken);

    // Account deletion: removes the shares where the user is the recipient or the one who shared
    Task RemoveAllForUserAsync(Guid userId, CancellationToken cancellationToken);
}

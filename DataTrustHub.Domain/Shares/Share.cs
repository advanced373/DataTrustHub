using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Shares;

// Read access to one data item for one recipient, given by the item's owner (ADR-0015). Its own aggregate:
// a share is created and revoked on its own, and read per recipient. It references the data item and the
// users by id. Who may share whom (S1, S2) and one active share per item and recipient (S3) are checked by
// the use cases; the expiry (S4) and the active rule (S5) live here
public sealed class Share
{
    // Rebuilds a stored entity; used by the persistence mappers only (internal, visible to Infrastructure)
    internal Share(
        Guid id,
        Guid dataItemId,
        Guid recipientUserId,
        Guid sharedByUserId,
        DateTime sharedAt,
        DateTime? expiresAt,
        DateTime? revokedAt)
    {
        Id = id;
        DataItemId = dataItemId;
        RecipientUserId = recipientUserId;
        SharedByUserId = sharedByUserId;
        SharedAt = sharedAt;
        ExpiresAt = expiresAt;
        RevokedAt = revokedAt;
    }

    public Guid Id { get; private set; }

    public Guid DataItemId { get; private set; }

    public Guid RecipientUserId { get; private set; }

    public Guid SharedByUserId { get; private set; }

    // UTC
    public DateTime SharedAt { get; private set; }

    // UTC; null for a share without an expiry date
    public DateTime? ExpiresAt { get; private set; }

    // UTC; null while the share has not been revoked
    public DateTime? RevokedAt { get; private set; }

    // Invariant S4: an expiry date, when given, is later than the moment of sharing
    public static Result<Share> Create(
        Guid dataItemId,
        Guid recipientUserId,
        Guid sharedByUserId,
        DateTime sharedAt,
        DateTime? expiresAt)
    {
        if (expiresAt is DateTime expiry && expiry <= sharedAt)
            return Result.Failure<Share>(ShareErrors.ExpiryNotAfterSharedAt);

        return Result.Success(new Share(
            Guid.NewGuid(), dataItemId, recipientUserId, sharedByUserId, sharedAt, expiresAt, revokedAt: null));
    }

    // Invariant S5, the share's own part: not revoked and not expired. Whether its item is deleted is checked
    // by the read rule. IShareRepository.HasActiveAsync applies the same comparison in the database
    public bool IsActiveAt(DateTime now) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);

    // A share already revoked answers NotFound, as if it did not exist, and keeps its first revocation time
    public Result Revoke(DateTime revokedAt)
    {
        if (RevokedAt is not null) return Result.Failure(ShareErrors.NotFound);

        RevokedAt = revokedAt;
        return Result.Success();
    }
}

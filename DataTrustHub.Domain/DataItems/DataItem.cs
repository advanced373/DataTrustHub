using DataTrustHub.Domain.Clearances;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.DataItems;

// A stored file. Personal items have no policy and only their owner reads them; classified items
// belong to a policy and a level, and are read only by holders of a clearance on that policy at or
// above the level. The owner has no special right on a classified item (ADR-0014, D3'). The SQL form
// of the read rule lives in the read side; a test keeps both forms in line (ADR-0012).
public sealed class DataItem
{
    public const string PersonalMarking = "UNCLASSIFIED";

    // Rebuilds a stored entity; used by the persistence mappers only (internal, visible to Infrastructure)
    internal DataItem(
        Guid id,
        string name,
        long size,
        string? content,
        Guid ownerUserId,
        string securityMarking,
        Guid? policyId,
        int? classificationPriority,
        bool isDeleted)
    {
        Id = id;
        Name = name;
        Size = size;
        Content = content;
        OwnerUserId = ownerUserId;
        SecurityMarking = securityMarking;
        PolicyId = policyId;
        ClassificationPriority = classificationPriority;
        IsDeleted = isDeleted;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public long Size { get; private set; }

    // Base64-encoded file content
    public string? Content { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public string SecurityMarking { get; private set; }

    public Guid? PolicyId { get; private set; }

    public int? ClassificationPriority { get; private set; }

    public bool IsDeleted { get; private set; }

    public bool IsClassified => PolicyId is not null;

    public static DataItem CreatePersonal(string name, long size, string content, Guid ownerUserId) =>
        new(Guid.NewGuid(), name, size, content, ownerUserId, PersonalMarking, null, null, isDeleted: false);

    // The owner may classify only under a policy they hold a clearance on, at most at that clearance's level
    public static Result<DataItem> CreateClassified(
        string name,
        long size,
        string content,
        Guid ownerUserId,
        Guid policyId,
        ClassificationLevel level,
        IEnumerable<Clearance> ownerClearances)
    {
        if (!ownerClearances.Any(c => c.UserId == ownerUserId && c.Allows(policyId, level.Priority)))
            return Result.Failure<DataItem>(DataItemErrors.NotCleared);

        return Result.Success(new DataItem(
            Guid.NewGuid(), name, size, content, ownerUserId, level.Name, policyId, level.Priority, isDeleted: false));
    }

    // A personal item is read by its owner only; a classified one through a clearance only, owner included
    public bool IsReadableBy(Guid userId, IEnumerable<Clearance> userClearances)
    {
        if (IsDeleted) return false;
        if (PolicyId is not Guid policyId) return OwnerUserId == userId;

        return userClearances.Any(c => c.UserId == userId && c.Allows(policyId, ClassificationPriority!.Value));
    }

    // Only the owner deletes; anyone else, or a second delete, sees "not found" so nothing is revealed
    public Result MarkDeleted(Guid requesterId)
    {
        if (IsDeleted || requesterId != OwnerUserId) return Result.Failure(DataItemErrors.NotFound);

        IsDeleted = true;
        return Result.Success();
    }
}

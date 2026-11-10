namespace DataTrustHub.Domain.ReadModels.DataItems;

// A data item the requester may read, with its content. OrganizationId is the organization of its policy, for
// the audit record of the download (ADR-0016); it and PolicyName are null for a personal item
public sealed record ReadableDataItem(
    Guid Id,
    string Name,
    long Size,
    string SecurityMarking,
    string? Content,
    int? ClassificationPriority,
    string? PolicyName,
    Guid? OrganizationId,
    bool IsOwner,
    bool CanDelete);

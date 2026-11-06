namespace DataTrustHub.Domain.ReadModels.DataItems;

// A data item in the requester's list. Policy fields are null for a personal item; CanDelete is the delete
// rule (D5′) for the requester, IsOwner whether they uploaded it
public sealed record DataItemListEntry(
    Guid Id,
    string Name,
    long Size,
    string SecurityMarking,
    int? ClassificationPriority,
    Guid? PolicyId,
    string? PolicyName,
    string? OwnerEmail,
    bool IsOwner,
    bool CanDelete);

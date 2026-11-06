namespace DataTrustHub.Domain.ReadModels.Clearances;

// A clearance on a policy of an organization, with its classification level, as its organization admin sees
// it in the list and on its own page
public sealed record ClearanceEntry(
    Guid Id,
    string Name,
    Guid UserId,
    Guid PolicyId,
    string ClassificationLevelName,
    int ClassificationLevelPriority);

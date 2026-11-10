namespace DataTrustHub.Domain.ReadModels.DataItems;

// A data item that exists, as the target of a denied read. OrganizationId is the organization of its policy
// while that policy exists, null for a personal item or one whose policy is gone (ADR-0016)
public sealed record DeniedReadTarget(Guid? OrganizationId);

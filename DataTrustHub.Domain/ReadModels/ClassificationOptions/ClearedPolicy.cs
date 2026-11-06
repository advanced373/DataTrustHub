namespace DataTrustHub.Domain.ReadModels.ClassificationOptions;

// A policy the user holds a clearance on, and the highest priority of their clearances there
public sealed record ClearedPolicy(Guid PolicyId, string PolicyName, string OrganizationName, int MaxPriority);

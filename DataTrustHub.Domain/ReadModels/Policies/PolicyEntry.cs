namespace DataTrustHub.Domain.ReadModels.Policies;

// A policy of an organization, as its organization admin sees it in the list and on its own page
public sealed record PolicyEntry(Guid Id, string Name, Guid OrganizationId);

namespace DataTrustHub.Domain.ReadModels.Organizations;

// An organization (tenant), as the platform admin sees it in the list and on its own page
public sealed record OrganizationEntry(Guid Id, string Name);

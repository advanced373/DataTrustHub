namespace DataTrustHub.Domain.ReadModels.Memberships;

// One of the user's own organizations, with their membership there and its role (ADR-0014)
public sealed record MyOrganization(Guid OrganizationId, string OrganizationName, Guid MembershipId, string Role);

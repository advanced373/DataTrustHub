namespace DataTrustHub.Domain.ReadModels.Memberships;

// A membership in one organization, with the email of its user and the membership role
public sealed record OrganizationMember(Guid MembershipId, Guid UserId, string Email, string Role);

using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Memberships;

// A user's membership in one organization, with a role there (ADR-0014)
public sealed class Membership
{
    // Rebuilds a stored entity; used by the persistence mappers only (internal, visible to Infrastructure)
    internal Membership(Guid id, Guid userId, Guid organizationId, MembershipRole role)
    {
        Id = id;
        UserId = userId;
        OrganizationId = organizationId;
        Role = role;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid OrganizationId { get; private set; }

    public MembershipRole Role { get; private set; }

    public static Membership AddMember(Guid userId, Guid organizationId) =>
        new(Guid.NewGuid(), userId, organizationId, MembershipRole.Member);

    public static Membership AddGuest(Guid userId, Guid organizationId) =>
        new(Guid.NewGuid(), userId, organizationId, MembershipRole.Guest);

    public static Membership AppointOrgAdmin(Guid userId, Guid organizationId) =>
        new(Guid.NewGuid(), userId, organizationId, MembershipRole.OrgAdmin);

    // Invariant M3: a guest cannot become an organization admin. The last organization admin (M2)
    // needs the other memberships, so the use case checks it
    public Result ChangeRole(MembershipRole newRole)
    {
        if (Role.IsGuest && newRole.IsOrgAdmin) return Result.Failure(MembershipErrors.GuestCannotBeOrgAdmin);

        Role = newRole;
        return Result.Success();
    }
}

namespace DataTrustHub.Domain.Memberships;

// A user's role inside one organization (ADR-0014); stored by name. The set is closed: the private
// constructor allows only the standard roles below
public sealed record MembershipRole
{
    private MembershipRole(string name) => Name = name;

    public string Name { get; }

    public static readonly MembershipRole Member = new("Member");

    // An external person, added explicitly; works like a member but never manages the organization
    public static readonly MembershipRole Guest = new("Guest");

    // Manages the organization: its members, policies and clearances
    public static readonly MembershipRole OrgAdmin = new("OrgAdmin");

    public static readonly IReadOnlyList<MembershipRole> Standard = [Member, Guest, OrgAdmin];

    public bool IsOrgAdmin => this == OrgAdmin;

    public bool IsGuest => this == Guest;

    public static MembershipRole? FindStandard(string name) =>
        Standard.FirstOrDefault(role => role.Name == name);
}

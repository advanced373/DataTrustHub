using DataTrustHub.Domain.Memberships;

namespace DataTrustHub.Domain.DataItems;

// What a data item cannot know about the requester when they delete it (D5'): whether its organization
// (its policy's organization) still exists, and the requester's membership there. The use case gathers it.
// Built only through the two factories below, so no contradictory combination can exist
public sealed record DeletionRights
{
    private DeletionRights(bool itsOrganizationExists, bool isMemberOfItsOrganization, bool isOrgAdminOfItsOrganization)
    {
        ItsOrganizationExists = itsOrganizationExists;
        IsMemberOfItsOrganization = isMemberOfItsOrganization;
        IsOrgAdminOfItsOrganization = isOrgAdminOfItsOrganization;
    }

    public bool ItsOrganizationExists { get; }

    public bool IsMemberOfItsOrganization { get; }

    public bool IsOrgAdminOfItsOrganization { get; }

    // A personal item, or a classified item whose policy no longer exists: only the owner may delete it
    public static readonly DeletionRights WithoutOrganization = new(false, false, false);

    // The requester's membership in the item's organization, or null when they are not a member
    public static DeletionRights InOrganization(Membership? requesterMembership) =>
        new(true, requesterMembership is not null, requesterMembership?.Role.IsOrgAdmin == true);
}

using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.DataItems;
using DataTrustHub.Domain.Memberships;
using Xunit;

namespace DataTrustHub.Features.Tests.Domain;

// The DataItem rules on the entity, without HTTP or a database: upload within the owner's clearance,
// the read rule D3' (ADR-0014): classified items are read only through a clearance, owner included, and the
// delete rule D5': a classified item is deleted by its owner while a member of its organization, or by an
// organization admin of its organization; a personal one by its owner only
public class DataItemTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Reader = Guid.NewGuid();
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid PolicyId = Guid.NewGuid();
    private static readonly ClassificationLevel Internal = ClassificationLevel.FindStandard(1)!;
    private static readonly ClassificationLevel Confidential = ClassificationLevel.FindStandard(2)!;
    private static readonly ClassificationLevel Secret = ClassificationLevel.FindStandard(3)!;
    private static readonly ClassificationLevel TopSecret = ClassificationLevel.FindStandard(4)!;

    [Fact]
    public void CreateClassified_WithinOwnersClearance_SetsPolicyLevelAndMarking()
    {
        var result = DataItem.CreateClassified("r.txt", 1, "eA==", Owner, PolicyId, Confidential, [Grant(Owner, Secret)]);

        Assert.True(result.IsSuccess);
        Assert.Equal((PolicyId, 2, "Confidential"), (result.Value.PolicyId, result.Value.ClassificationPriority, result.Value.SecurityMarking));
    }

    [Fact]
    public void CreateClassified_AboveOwnersClearanceOrOnAnotherPolicy_Fails()
    {
        var above = DataItem.CreateClassified("r.txt", 1, "eA==", Owner, PolicyId, Secret, [Grant(Owner, Confidential)]);
        var otherPolicy = DataItem.CreateClassified("r.txt", 1, "eA==", Owner, Guid.NewGuid(), Confidential, [Grant(Owner, Secret)]);

        Assert.Equal(DataItemErrors.NotCleared, above.Error);
        Assert.Equal(DataItemErrors.NotCleared, otherPolicy.Error);
    }

    [Fact]
    public void IsReadableBy_ReaderAtOrAboveLevel_ReturnsTrue()
    {
        var item = Classified(Confidential);

        Assert.True(item.IsReadableBy(Reader, [Grant(Reader, Secret)]));
        Assert.True(item.IsReadableBy(Reader, [Grant(Reader, Confidential)]));
    }

    [Fact]
    public void IsReadableBy_ReaderBelowLevelOnAnotherPolicyOrWithOthersClearance_ReturnsFalse()
    {
        var item = Classified(Confidential);

        Assert.False(item.IsReadableBy(Reader, [Grant(Reader, Internal)]));
        Assert.False(item.IsReadableBy(Reader, [Clearance.Grant("other", Reader, Guid.NewGuid(), Secret)]));
        Assert.False(item.IsReadableBy(Reader, [Grant(Owner, Secret)]));
    }

    // D3': the owner of a classified item has no bypass, they read it only through a clearance
    [Fact]
    public void IsReadableBy_OwnerOfClassifiedWithoutClearance_ReturnsFalse()
    {
        var item = Classified(Confidential);

        Assert.False(item.IsReadableBy(Owner, []));
        Assert.False(item.IsReadableBy(Owner, [Grant(Reader, Secret)]));
    }

    [Fact]
    public void IsReadableBy_OwnerOfClassifiedWithClearanceBelowLevel_ReturnsFalse()
    {
        var item = Classified(Confidential);

        Assert.False(item.IsReadableBy(Owner, [Grant(Owner, Internal)]));
    }

    [Fact]
    public void IsReadableBy_OwnerOfClassifiedWithClearanceOnAnotherPolicy_ReturnsFalse()
    {
        var item = Classified(Confidential);

        Assert.False(item.IsReadableBy(Owner, [Clearance.Grant("other", Owner, Guid.NewGuid(), Secret)]));
    }

    [Fact]
    public void IsReadableBy_OwnerOfClassifiedWithClearanceAtOrAboveLevel_ReturnsTrue()
    {
        var item = Classified(Confidential);

        Assert.True(item.IsReadableBy(Owner, [Grant(Owner, Confidential)]));
        Assert.True(item.IsReadableBy(Owner, [Grant(Owner, Secret)]));
    }

    [Fact]
    public void IsReadableBy_PersonalItemOwnerWithoutClearance_ReturnsTrue()
    {
        var item = DataItem.CreatePersonal("p.txt", 1, "eA==", Owner);

        Assert.True(item.IsReadableBy(Owner, []));
    }

    [Fact]
    public void IsReadableBy_PersonalItemNonOwnerWithClearances_ReturnsFalse()
    {
        var item = DataItem.CreatePersonal("p.txt", 1, "eA==", Owner);

        Assert.False(item.IsReadableBy(Reader, [Grant(Reader, Secret)]));
        Assert.False(item.IsReadableBy(Reader, [Grant(Reader, TopSecret), Grant(Owner, TopSecret)]));
    }

    [Fact]
    public void IsReadableBy_DeletedClassifiedItem_ReturnsFalseForEveryoneIncludingClearedOwner()
    {
        var item = Classified(Confidential);
        item.MarkDeleted(Owner, MemberRights(Owner));

        Assert.False(item.IsReadableBy(Owner, [Grant(Owner, TopSecret)]));
        Assert.False(item.IsReadableBy(Reader, [Grant(Reader, TopSecret)]));
    }

    [Fact]
    public void IsReadableBy_DeletedPersonalItem_ReturnsFalseForOwner()
    {
        var item = DataItem.CreatePersonal("p.txt", 1, "eA==", Owner);
        item.MarkDeleted(Owner, DeletionRights.WithoutOrganization);

        Assert.False(item.IsReadableBy(Owner, []));
    }

    // --- D5': personal items ---

    [Fact]
    public void MarkDeleted_PersonalItemByOwner_Succeeds()
    {
        var item = DataItem.CreatePersonal("p.txt", 1, "eA==", Owner);

        Assert.True(item.MarkDeleted(Owner, DeletionRights.WithoutOrganization).IsSuccess);
        Assert.True(item.IsDeleted);
    }

    [Fact]
    public void MarkDeleted_PersonalItemByNonOwner_ReturnsNotFound()
    {
        var item = DataItem.CreatePersonal("p.txt", 1, "eA==", Owner);

        Assert.Equal(DataItemErrors.NotFound, item.MarkDeleted(Reader, DeletionRights.WithoutOrganization).Error);
        Assert.False(item.IsDeleted);
    }

    // Even rights in some organization give nothing on a personal item
    [Fact]
    public void MarkDeleted_PersonalItemByOrgAdminNonOwner_ReturnsNotFound()
    {
        var item = DataItem.CreatePersonal("p.txt", 1, "eA==", Owner);

        Assert.Equal(DataItemErrors.NotFound, item.MarkDeleted(Reader, OrgAdminRights(Reader)).Error);
    }

    [Fact]
    public void MarkDeleted_PersonalItemSecondDeleteByOwner_ReturnsNotFound()
    {
        var item = DataItem.CreatePersonal("p.txt", 1, "eA==", Owner);
        item.MarkDeleted(Owner, DeletionRights.WithoutOrganization);

        Assert.Equal(DataItemErrors.NotFound, item.MarkDeleted(Owner, DeletionRights.WithoutOrganization).Error);
    }

    // --- D5': classified items whose organization exists ---

    [Fact]
    public void MarkDeleted_ClassifiedByOwnerWhoIsMember_Succeeds()
    {
        var item = Classified(Confidential);

        Assert.True(item.MarkDeleted(Owner, MemberRights(Owner)).IsSuccess);
        Assert.True(item.IsDeleted);
    }

    [Fact]
    public void MarkDeleted_ClassifiedByOwnerWhoIsGuest_Succeeds()
    {
        var item = Classified(Confidential);
        var rights = DeletionRights.InOrganization(Membership.AddGuest(Owner, OrganizationId));

        Assert.True(item.MarkDeleted(Owner, rights).IsSuccess);
    }

    [Fact]
    public void MarkDeleted_ClassifiedByOwnerWhoIsOrgAdmin_Succeeds()
    {
        var item = Classified(Confidential);

        Assert.True(item.MarkDeleted(Owner, OrgAdminRights(Owner)).IsSuccess);
    }

    // The owner who left the organization (or was removed from it) is no longer a member there
    [Fact]
    public void MarkDeleted_ClassifiedByOwnerWhoIsNotMember_ReturnsNotFound()
    {
        var item = Classified(Confidential);

        Assert.Equal(DataItemErrors.NotFound, item.MarkDeleted(Owner, DeletionRights.InOrganization(null)).Error);
        Assert.False(item.IsDeleted);
    }

    [Fact]
    public void MarkDeleted_ClassifiedByOrgAdminWhoIsNotOwner_Succeeds()
    {
        var item = Classified(Confidential);

        Assert.True(item.MarkDeleted(Reader, OrgAdminRights(Reader)).IsSuccess);
        Assert.True(item.IsDeleted);
    }

    // D5' needs no clearance: the organization admin may delete an item they cannot read
    [Fact]
    public void MarkDeleted_ClassifiedByOrgAdminWhoCannotRead_Succeeds()
    {
        var item = Classified(Secret);
        Assert.False(item.IsReadableBy(Reader, [Grant(Reader, Internal)]));

        Assert.True(item.MarkDeleted(Reader, OrgAdminRights(Reader)).IsSuccess);
    }

    [Fact]
    public void MarkDeleted_ClassifiedByMemberWhoIsNotOwner_ReturnsNotFound()
    {
        var item = Classified(Confidential);

        Assert.Equal(DataItemErrors.NotFound, item.MarkDeleted(Reader, MemberRights(Reader)).Error);
        Assert.False(item.IsDeleted);
    }

    [Fact]
    public void MarkDeleted_ClassifiedByGuestWhoIsNotOwner_ReturnsNotFound()
    {
        var item = Classified(Confidential);
        var rights = DeletionRights.InOrganization(Membership.AddGuest(Reader, OrganizationId));

        Assert.Equal(DataItemErrors.NotFound, item.MarkDeleted(Reader, rights).Error);
    }

    // A reader with a clearance at the item's level, but not a member of its organization
    [Fact]
    public void MarkDeleted_ClassifiedByOutsider_ReturnsNotFound()
    {
        var item = Classified(Confidential);
        Assert.True(item.IsReadableBy(Reader, [Grant(Reader, Secret)]));

        Assert.Equal(DataItemErrors.NotFound, item.MarkDeleted(Reader, DeletionRights.InOrganization(null)).Error);
        Assert.False(item.IsDeleted);
    }

    [Fact]
    public void MarkDeleted_ClassifiedAlreadyDeleted_ReturnsNotFoundEvenForOrgAdmin()
    {
        var item = Classified(Confidential);
        item.MarkDeleted(Owner, MemberRights(Owner));

        Assert.Equal(DataItemErrors.NotFound, item.MarkDeleted(Owner, MemberRights(Owner)).Error);
        Assert.Equal(DataItemErrors.NotFound, item.MarkDeleted(Reader, OrgAdminRights(Reader)).Error);
    }

    [Fact]
    public void MarkDeleted_ClassifiedByOrgAdmin_ThenUnreadableForClearedReader()
    {
        var item = Classified(Confidential);

        item.MarkDeleted(Reader, OrgAdminRights(Reader));

        Assert.False(item.IsReadableBy(Owner, [Grant(Owner, Secret)]));
    }

    // --- D5': classified items whose policy no longer exists (no organization) ---

    [Fact]
    public void MarkDeleted_ClassifiedPolicyGoneByOwner_Succeeds()
    {
        var item = Classified(Confidential);

        Assert.True(item.MarkDeleted(Owner, DeletionRights.WithoutOrganization).IsSuccess);
    }

    [Fact]
    public void MarkDeleted_ClassifiedPolicyGoneByNonOwner_ReturnsNotFound()
    {
        var item = Classified(Confidential);

        Assert.Equal(DataItemErrors.NotFound, item.MarkDeleted(Reader, DeletionRights.WithoutOrganization).Error);
        Assert.False(item.IsDeleted);
    }

    // --- DeletionRights, as the use case gathers them ---

    [Theory]
    [MemberData(nameof(RolesAndExpectedOrgAdmin))]
    public void DeletionRights_InOrganizationWithMembership_IsMemberAndOrgAdminOnlyForThatRole(
        string roleName, bool expectedOrgAdmin)
    {
        var role = MembershipRole.FindStandard(roleName)!;

        var rights = DeletionRights.InOrganization(MembershipWithRole(Reader, role));

        Assert.Equal((true, true, expectedOrgAdmin), Flags(rights));
    }

    [Fact]
    public void DeletionRights_InOrganizationWithoutMembership_IsNeitherMemberNorOrgAdmin()
    {
        Assert.Equal((true, false, false), Flags(DeletionRights.InOrganization(null)));
    }

    [Fact]
    public void DeletionRights_WithoutOrganization_HasNoOrganization()
    {
        Assert.Equal((false, false, false), Flags(DeletionRights.WithoutOrganization));
    }

    // Only the factories build it, so no contradictory combination (e.g. organization admin but not a member) exists
    [Fact]
    public void DeletionRights_HasNoPublicConstructor()
    {
        Assert.Empty(typeof(DeletionRights).GetConstructors());
    }

    public static TheoryData<string, bool> RolesAndExpectedOrgAdmin => new()
    {
        { MembershipRole.Member.Name, false },
        { MembershipRole.Guest.Name, false },
        { MembershipRole.OrgAdmin.Name, true }
    };

    private static (bool OrganizationExists, bool IsMember, bool IsOrgAdmin) Flags(DeletionRights rights) =>
        (rights.ItsOrganizationExists, rights.IsMemberOfItsOrganization, rights.IsOrgAdminOfItsOrganization);

    private static Membership MembershipWithRole(Guid userId, MembershipRole role)
    {
        if (role.IsOrgAdmin) return Membership.AppointOrgAdmin(userId, OrganizationId);
        return role.IsGuest ? Membership.AddGuest(userId, OrganizationId) : Membership.AddMember(userId, OrganizationId);
    }

    private static DataItem Classified(ClassificationLevel level) =>
        DataItem.CreateClassified("r.txt", 1, "eA==", Owner, PolicyId, level, [Grant(Owner, Secret)]).Value;

    private static Clearance Grant(Guid userId, ClassificationLevel level) =>
        Clearance.Grant("test", userId, PolicyId, level);

    private static DeletionRights MemberRights(Guid userId) =>
        DeletionRights.InOrganization(Membership.AddMember(userId, OrganizationId));

    private static DeletionRights OrgAdminRights(Guid userId) =>
        DeletionRights.InOrganization(Membership.AppointOrgAdmin(userId, OrganizationId));
}

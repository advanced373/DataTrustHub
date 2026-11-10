using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.ClearanceSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// #23 (UpdateClearance): an organization admin changes the name and the classification level of a clearance on a
// policy of their organization (C6, ADR-0014), never of their own (C8); the user and the policy stay, and the new
// level applies to data access right away (C1). Who may call is also covered in ClearanceAccessTests. Tests in a
// class run one after another, so "the audit records added by this request" is exact
public class UpdateClearanceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int MaxNameLength = 256;
    private const string UpdateClearanceAction = "UpdateClearance";
    private const string SucceededOutcome = "Succeeded";
    private const string RejectedOutcome = "Rejected";
    private const string NewName = "Changed clearance";
    private const string NewLevelName = "Top Secret";
    private const int NewLevelPriority = 4;
    private const string UploadEndpoint = "/data/upload";
    private const string ListEndpoint = "/data";
    private const string OwnerLevelName = "Top Secret";
    private const int OwnerLevelPriority = 4;
    private const string LowLevelName = "Internal";
    private const int LowLevelPriority = 1;
    private const string HighLevelName = "Secret";
    private const int HighLevelPriority = 3;

    public static TheoryData<string> NotOrgAdminCallers => new(OrganizationCallers.NotOrgAdminKinds);

    // --- AC1: an organization admin changes another user's clearance ---

    [Fact]
    public async Task UpdateClearance_OrgAdminOnAnotherUsersClearance_Returns204()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_ok");

        var response = await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, clearanceId, NewName, NewLevelName, NewLevelPriority);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task UpdateClearance_OrgAdminOnAnotherUsersClearance_GetReturnsTheNewNameAndLevelWithTheSameUserAndPolicy()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_get");

        await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, clearanceId, NewName, NewLevelName, NewLevelPriority);

        Assert.Equal(
            new ClearanceResponse(clearanceId, NewName, grant.MemberId, grant.PolicyId, NewLevelName, NewLevelPriority),
            await GetClearanceAsync(grant, clearanceId));
    }

    // Only the name changes; the level stays
    [Fact]
    public async Task UpdateClearance_NewNameOnly_GetReturnsTheNewNameAndTheSameLevel()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_name_only");

        await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, clearanceId, NewName, DefaultLevelName, DefaultLevelPriority);

        Assert.Equal(
            new ClearanceResponse(clearanceId, NewName, grant.MemberId, grant.PolicyId, DefaultLevelName, DefaultLevelPriority),
            await GetClearanceAsync(grant, clearanceId));
    }

    [Fact]
    public async Task UpdateClearance_ByAnotherOrgAdminOfTheOrganization_Returns204AndStoresIt()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_second_admin");
        var (second, _) = await OrganizationCallers.AddSecondOrgAdminAsync(factory, grant.Organization, Prefix("update_clearance_second"));

        var response = await UpdateAsync(second, grant.OrganizationId, clearanceId, NewName, NewLevelName, NewLevelPriority);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal((NewName, NewLevelPriority), NameAndPriority(await GetClearanceAsync(grant, clearanceId)));
    }

    [Fact]
    public async Task UpdateClearance_DoesNotAddOrRemoveAClearance()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_count");
        var before = (await StoredClearancesAsync(factory)).Select(c => (c.Id, c.UserId, c.PolicyId)).ToHashSet();

        (await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, clearanceId, NewName, NewLevelName, NewLevelPriority))
            .EnsureSuccessStatusCode();

        Assert.Equal(before, (await StoredClearancesAsync(factory)).Select(c => (c.Id, c.UserId, c.PolicyId)).ToHashSet());
    }

    // --- AC2: the new level takes effect for data access right away (C1) ---

    [Fact]
    public async Task UpdateClearance_RaisedLevel_UserReadsAnItemAtTheNewPriority()
    {
        var access = await ArrangeAccessAsync("update_clearance_raise", LowLevelName, LowLevelPriority);
        Assert.Equal(HttpStatusCode.NotFound, (await access.Reader.GetAsync(ItemEndpoint(access.ItemId))).StatusCode);

        (await UpdateAsync(access.Grant.OrgAdmin, access.Grant.OrganizationId, access.ClearanceId, NewName, HighLevelName, HighLevelPriority))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await access.Reader.GetAsync(ItemEndpoint(access.ItemId))).StatusCode);
    }

    [Fact]
    public async Task UpdateClearance_RaisedLevel_ListShowsTheItemAtTheNewPriority()
    {
        var access = await ArrangeAccessAsync("update_clearance_raise_list", LowLevelName, LowLevelPriority);

        (await UpdateAsync(access.Grant.OrgAdmin, access.Grant.OrganizationId, access.ClearanceId, NewName, HighLevelName, HighLevelPriority))
            .EnsureSuccessStatusCode();

        Assert.Contains(await ListItemsAsync(access.Reader), i => i.Id == access.ItemId);
    }

    [Fact]
    public async Task UpdateClearance_LoweredLevel_ItemAboveTheNewPriorityIsNoLongerReadable()
    {
        var access = await ArrangeAccessAsync("update_clearance_lower", HighLevelName, HighLevelPriority);
        Assert.Equal(HttpStatusCode.OK, (await access.Reader.GetAsync(ItemEndpoint(access.ItemId))).StatusCode);

        (await UpdateAsync(access.Grant.OrgAdmin, access.Grant.OrganizationId, access.ClearanceId, NewName, LowLevelName, LowLevelPriority))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await access.Reader.GetAsync(ItemEndpoint(access.ItemId))).StatusCode);
    }

    [Fact]
    public async Task UpdateClearance_LoweredLevel_ListNoLongerShowsTheItemAboveTheNewPriority()
    {
        var access = await ArrangeAccessAsync("update_clearance_lower_list", HighLevelName, HighLevelPriority);

        (await UpdateAsync(access.Grant.OrgAdmin, access.Grant.OrganizationId, access.ClearanceId, NewName, LowLevelName, LowLevelPriority))
            .EnsureSuccessStatusCode();

        Assert.DoesNotContain(await ListItemsAsync(access.Reader), i => i.Id == access.ItemId);
    }

    // Boundary of C1: a level equal to the item's priority covers it
    [Fact]
    public async Task UpdateClearance_LoweredToTheItemsPriority_ItemStaysReadable()
    {
        var access = await ArrangeAccessAsync("update_clearance_lower_equal", NewLevelName, NewLevelPriority);

        (await UpdateAsync(access.Grant.OrgAdmin, access.Grant.OrganizationId, access.ClearanceId, NewName, HighLevelName, HighLevelPriority))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await access.Reader.GetAsync(ItemEndpoint(access.ItemId))).StatusCode);
    }

    // --- AC3: one's own clearance (C8, four-eyes) ---

    [Fact]
    public async Task UpdateClearance_OwnClearance_Returns409CannotChangeOwn()
    {
        var (grant, ownClearanceId) = await ArrangeOwnClearanceAsync("update_clearance_own");

        var response = await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, ownClearanceId, NewName, NewLevelName, NewLevelPriority);

        await AssertCannotChangeOwnAsync(response);
    }

    [Fact]
    public async Task UpdateClearance_OwnClearance_KeepsTheClearance()
    {
        var (grant, ownClearanceId) = await ArrangeOwnClearanceAsync("update_clearance_own_kept");
        var before = await GetClearanceAsync(grant, ownClearanceId);

        await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, ownClearanceId, NewName, NewLevelName, NewLevelPriority);

        Assert.Equal(before, await GetClearanceAsync(grant, ownClearanceId));
    }

    // C8 holds even when the values are the current ones
    [Fact]
    public async Task UpdateClearance_OwnClearanceWithItsCurrentValues_Returns409CannotChangeOwn()
    {
        var (grant, ownClearanceId) = await ArrangeOwnClearanceAsync("update_clearance_own_same");
        var current = await GetClearanceAsync(grant, ownClearanceId);

        var response = await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, ownClearanceId,
            current.Name, current.ClassificationLevelName, current.ClassificationLevelPriority);

        await AssertCannotChangeOwnAsync(response);
    }

    // --- AC4: a caller who is not an organization admin of the organization ---

    [Theory]
    [MemberData(nameof(NotOrgAdminCallers))]
    public async Task UpdateClearance_CallerNotOrgAdmin_Returns404OrganizationNotFound(string callerKind)
    {
        var prefix = Prefix($"update_clearance_caller_{callerKind}");
        var (grant, clearanceId) = await ArrangeAsync(prefix);
        var caller = await OrganizationCallers.CreateNotOrgAdminAsync(factory, grant.Organization, callerKind, prefix);

        var response = await UpdateAsync(caller, grant.OrganizationId, clearanceId, NewName, NewLevelName, NewLevelPriority);

        await AssertOrganizationNotFoundAsync(response);
    }

    [Theory]
    [MemberData(nameof(NotOrgAdminCallers))]
    public async Task UpdateClearance_CallerNotOrgAdmin_KeepsTheClearance(string callerKind)
    {
        var prefix = Prefix($"update_clearance_caller_kept_{callerKind}");
        var (grant, clearanceId) = await ArrangeAsync(prefix);
        var caller = await OrganizationCallers.CreateNotOrgAdminAsync(factory, grant.Organization, callerKind, prefix);
        var before = await GetClearanceAsync(grant, clearanceId);

        await UpdateAsync(caller, grant.OrganizationId, clearanceId, NewName, NewLevelName, NewLevelPriority);

        Assert.Equal(before, await GetClearanceAsync(grant, clearanceId));
    }

    // The member changes their own clearance: the caller check must answer, not C8
    [Fact]
    public async Task UpdateClearance_MemberOnOwnClearance_Returns404OrganizationNotFound()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_member_own");

        var response = await UpdateAsync(grant.Member, grant.OrganizationId, clearanceId, NewName, NewLevelName, NewLevelPriority);

        await AssertOrganizationNotFoundAsync(response);
    }

    [Fact]
    public async Task UpdateClearance_MissingOrganization_Returns404OrganizationNotFound()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_missing_org");

        var response = await UpdateAsync(grant.OrgAdmin, Guid.NewGuid(), clearanceId, NewName, NewLevelName, NewLevelPriority);

        await AssertOrganizationNotFoundAsync(response);
    }

    [Fact]
    public async Task UpdateClearance_PlatformAdminAndMissingOrganization_Returns404OrganizationNotFound()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_missing_org_platform");

        var response = await UpdateAsync(
            grant.Organization.PlatformAdmin, Guid.NewGuid(), clearanceId, NewName, NewLevelName, NewLevelPriority);

        await AssertOrganizationNotFoundAsync(response);
    }

    // --- AC5: an unknown clearance, or one on a policy of another organization ---

    [Fact]
    public async Task UpdateClearance_UnknownClearance_Returns404ClearanceNotFound()
    {
        var (grant, _) = await ArrangeAsync("update_clearance_unknown");

        var response = await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, Guid.NewGuid(), NewName, NewLevelName, NewLevelPriority);

        await AssertClearanceNotFoundAsync(response);
    }

    // The caller is an organization admin of both organizations, so only the scope of the route can refuse
    [Fact]
    public async Task UpdateClearance_ClearanceOfAnotherOrganizationThroughOwnRoute_Returns404ClearanceNotFound()
    {
        var (grant, foreignClearanceId, _) = await ArrangeForeignClearanceAsync("update_clearance_foreign");

        var response = await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, foreignClearanceId, NewName, NewLevelName, NewLevelPriority);

        await AssertClearanceNotFoundAsync(response);
    }

    [Fact]
    public async Task UpdateClearance_ClearanceOfAnotherOrganizationThroughOwnRoute_KeepsIt()
    {
        var (grant, foreignClearanceId, other) = await ArrangeForeignClearanceAsync("update_clearance_foreign_kept");
        var route = ClearanceEndpoint(other.OrganizationId, foreignClearanceId);
        var before = await other.OrgAdmin.GetFromJsonAsync<ClearanceResponse>(route);

        await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, foreignClearanceId, NewName, NewLevelName, NewLevelPriority);

        Assert.Equal(before, await other.OrgAdmin.GetFromJsonAsync<ClearanceResponse>(route));
    }

    // --- AC6: validation, and no token ---

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task UpdateClearance_EmptyName_Returns400(string? name)
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_empty_name");

        var response = await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, clearanceId, name, NewLevelName, NewLevelPriority);

        await AssertValidationErrorAsync(response);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task UpdateClearance_EmptyLevelName_Returns400(string? levelName)
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_empty_level");

        var response = await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, clearanceId, NewName, levelName, NewLevelPriority);

        await AssertValidationErrorAsync(response);
    }

    [Fact]
    public async Task UpdateClearance_NameLongerThan256_Returns400AndKeepsTheClearance()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_long_name");
        var before = await GetClearanceAsync(grant, clearanceId);

        var response = await UpdateAsync(
            grant.OrgAdmin, grant.OrganizationId, clearanceId, new string('a', MaxNameLength + 1), NewLevelName, NewLevelPriority);

        await AssertValidationErrorAsync(response);
        Assert.Equal(before, await GetClearanceAsync(grant, clearanceId));
    }

    [Fact]
    public async Task UpdateClearance_LevelNameLongerThan256_Returns400AndKeepsTheClearance()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_long_level");
        var before = await GetClearanceAsync(grant, clearanceId);

        var response = await UpdateAsync(
            grant.OrgAdmin, grant.OrganizationId, clearanceId, NewName, new string('a', MaxNameLength + 1), NewLevelPriority);

        await AssertValidationErrorAsync(response);
        Assert.Equal(before, await GetClearanceAsync(grant, clearanceId));
    }

    // Invariant C4
    [Fact]
    public async Task UpdateClearance_NegativePriority_Returns400AndKeepsTheClearance()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_negative");
        var before = await GetClearanceAsync(grant, clearanceId);

        var response = await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, clearanceId, NewName, NewLevelName, -1);

        await AssertValidationErrorAsync(response);
        Assert.Equal(before, await GetClearanceAsync(grant, clearanceId));
    }

    // The boundaries: 256 characters and priority 0 are valid
    [Fact]
    public async Task UpdateClearance_NamesOf256AndPriorityZero_Returns204AndStoresThem()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_bounds");
        var name = new string('n', MaxNameLength);
        var levelName = $"{Guid.NewGuid():N}".PadRight(MaxNameLength, 'l');

        var response = await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, clearanceId, name, levelName, 0);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            new ClearanceResponse(clearanceId, name, grant.MemberId, grant.PolicyId, levelName, 0),
            await GetClearanceAsync(grant, clearanceId));
    }

    [Fact]
    public async Task UpdateClearance_EmptyClearanceId_Returns400()
    {
        var (grant, _) = await ArrangeAsync("update_clearance_empty_id");

        var response = await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, Guid.Empty, NewName, NewLevelName, NewLevelPriority);

        await AssertValidationErrorAsync(response);
    }

    [Fact]
    public async Task UpdateClearance_Anonymous_Returns401AndKeepsTheClearance()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_anonymous");
        var before = await GetClearanceAsync(grant, clearanceId);

        var response = await UpdateAsync(
            factory.CreateClient(), grant.OrganizationId, clearanceId, NewName, NewLevelName, NewLevelPriority);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, await GetClearanceAsync(grant, clearanceId));
    }

    // --- AC7: a stored level is reused; other clearances keep the old level ---

    [Fact]
    public async Task UpdateClearance_ToAStoredLevel_ReusesItsRow()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_reuse");
        var storedLevel = UniqueLevelName("Reused");
        var (_, holderId) = await AddUserWithRoleAsync(factory, grant.Organization, Prefix("update_clearance_reuse_holder"), MemberRole);
        await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, holderId, grant.PolicyId, storedLevel, NewLevelPriority);
        var levelId = Assert.Single(await LevelRowsAsync(storedLevel, NewLevelPriority)).Id;

        (await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, clearanceId, NewName, storedLevel, NewLevelPriority))
            .EnsureSuccessStatusCode();

        Assert.Equal(levelId, Assert.Single(await LevelRowsAsync(storedLevel, NewLevelPriority)).Id);
        Assert.Equal(levelId, await LevelIdOfAsync(clearanceId));
    }

    [Fact]
    public async Task UpdateClearance_ToANewLevel_StoresItOnce()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_new_level");
        var newLevel = UniqueLevelName("New");

        (await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, clearanceId, NewName, newLevel, NewLevelPriority))
            .EnsureSuccessStatusCode();

        Assert.Equal(await LevelIdOfAsync(clearanceId), Assert.Single(await LevelRowsAsync(newLevel, NewLevelPriority)).Id);
    }

    // The same name with another priority is another level
    [Fact]
    public async Task UpdateClearance_SameLevelNameOtherPriority_PointsAtALevelWithTheNewPriority()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_other_priority");

        (await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, clearanceId, NewName, DefaultLevelName, NewLevelPriority))
            .EnsureSuccessStatusCode();

        Assert.Equal((DefaultLevelName, NewLevelPriority), LevelOf(await GetClearanceAsync(grant, clearanceId)));
    }

    [Fact]
    public async Task UpdateClearance_OtherClearanceOnTheOldLevel_KeepsIt()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_shared_old");
        var (_, holderId) = await AddUserWithRoleAsync(factory, grant.Organization, Prefix("update_clearance_shared_holder"), MemberRole);
        var otherClearanceId = await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, holderId, grant.PolicyId);
        var oldLevelId = await LevelIdOfAsync(otherClearanceId);

        (await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, clearanceId, NewName, NewLevelName, NewLevelPriority))
            .EnsureSuccessStatusCode();

        Assert.Equal((DefaultLevelName, DefaultLevelPriority), LevelOf(await GetClearanceAsync(grant, otherClearanceId)));
        Assert.Equal(oldLevelId, await LevelIdOfAsync(otherClearanceId));
    }

    [Fact]
    public async Task UpdateClearance_OldLevelRow_IsNotEdited()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_old_row");
        var oldLevelId = await LevelIdOfAsync(clearanceId);

        (await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, clearanceId, NewName, NewLevelName, NewLevelPriority))
            .EnsureSuccessStatusCode();

        var oldRow = Assert.Single(await AllLevelRowsAsync(), l => l.Id == oldLevelId);
        Assert.Equal((DefaultLevelName, DefaultLevelPriority), (oldRow.Name, oldRow.Priority));
    }

    // --- AC8: audited with the clearance as its target, scoped to the organization ---

    [Fact]
    public async Task UpdateClearance_Succeeded_RecordsTheActorAndTheClearanceAsTarget()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_audit");

        var records = await RecordsAddedByAsync(() =>
            UpdateAsync(grant.OrgAdmin, grant.OrganizationId, clearanceId, NewName, NewLevelName, NewLevelPriority));

        var record = Assert.Single(records);
        Assert.Equal(
            (UpdateClearanceAction, (Guid?)grant.Organization.OrgAdminId, SucceededOutcome, (string?)null),
            (record.Action, record.ActorUserId, record.Outcome, record.ErrorCode));
        Assert.Equal(((string?)AuditTargetTypes.Clearance, (Guid?)clearanceId), (record.TargetType, record.TargetId));
    }

    [Fact]
    public async Task UpdateClearance_Succeeded_RecordIsScopedToTheOrganization()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_audit_scope");

        var records = await RecordsAddedByAsync(() =>
            UpdateAsync(grant.OrgAdmin, grant.OrganizationId, clearanceId, NewName, NewLevelName, NewLevelPriority));

        Assert.Equal(grant.OrganizationId, Assert.Single(records).OrganizationId);
    }

    [Fact]
    public async Task UpdateClearance_OwnClearance_RecordsTheRejectionWithTheTarget()
    {
        var (grant, ownClearanceId) = await ArrangeOwnClearanceAsync("update_clearance_audit_own");

        var records = await RecordsAddedByAsync(() =>
            UpdateAsync(grant.OrgAdmin, grant.OrganizationId, ownClearanceId, NewName, NewLevelName, NewLevelPriority));

        var record = Assert.Single(records);
        Assert.Equal(
            (UpdateClearanceAction, RejectedOutcome, (string?)AdminErrorCodes.ClearanceCannotChangeOwn, (Guid?)grant.OrganizationId),
            (record.Action, record.Outcome, record.ErrorCode, record.OrganizationId));
        Assert.Equal(((string?)AuditTargetTypes.Clearance, (Guid?)ownClearanceId), (record.TargetType, record.TargetId));
    }

    // --- AC9: the clearance's current values ---

    [Fact]
    public async Task UpdateClearance_CurrentValues_Returns204AndChangesNothing()
    {
        var (grant, clearanceId) = await ArrangeAsync("update_clearance_same");
        var before = await GetClearanceAsync(grant, clearanceId);
        var levelId = await LevelIdOfAsync(clearanceId);
        var levelCount = (await AllLevelRowsAsync()).Count;

        var response = await UpdateAsync(grant.OrgAdmin, grant.OrganizationId, clearanceId,
            before.Name, before.ClassificationLevelName, before.ClassificationLevelPriority);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(before, await GetClearanceAsync(grant, clearanceId));
        Assert.Equal((levelId, levelCount), (await LevelIdOfAsync(clearanceId), (await AllLevelRowsAsync()).Count));
    }

    // --- helpers ---

    private static string Prefix(string name) => $"{name}_{Guid.NewGuid():N}";

    private static string UniqueLevelName(string name) => $"{name} level {Guid.NewGuid():N}";

    private static string ItemEndpoint(Guid itemId) => $"{ListEndpoint}/{itemId}";

    // A grant scenario where the member holds a clearance at the default level, granted by the organization admin
    private async Task<(GrantScenario Grant, Guid ClearanceId)> ArrangeAsync(string prefix)
    {
        var grant = await CreateGrantScenarioAsync(factory, Prefix(prefix));
        return (grant, await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, grant.MemberId, grant.PolicyId));
    }

    // The organization admin holds a clearance, granted by a second organization admin (C8)
    private async Task<(GrantScenario Grant, Guid OwnClearanceId)> ArrangeOwnClearanceAsync(string prefix)
    {
        var grant = await CreateGrantScenarioAsync(factory, Prefix(prefix));
        var (second, _) = await OrganizationCallers.AddSecondOrgAdminAsync(factory, grant.Organization, Prefix($"{prefix}_second"));
        return (grant, await CreateClearanceAsync(second, grant.OrganizationId, grant.Organization.OrgAdminId, grant.PolicyId));
    }

    // The caller is organization admin of the scenario's organization and of another one, where the member holds a clearance
    private async Task<(GrantScenario Grant, Guid ForeignClearanceId, OrganizationScenario Other)> ArrangeForeignClearanceAsync(
        string prefix)
    {
        var grant = await CreateGrantScenarioAsync(factory, Prefix(prefix));
        var other = await CreateOrganizationWithOrgAdminAsync(factory, Prefix($"{prefix}_other"));
        var foreignPolicyId = await CreatePolicyAsync(other.OrgAdmin, $"{prefix} other policy", other.OrganizationId);
        var foreignClearanceId = await AddMemberAndGrantClearanceAsync(
            other.OrgAdmin, other.OrganizationId, grant.MemberId, foreignPolicyId, DefaultLevelName, DefaultLevelPriority);
        await SeedMembershipAsync(factory, Membership.AppointOrgAdmin(grant.Organization.OrgAdminId, other.OrganizationId));
        return (grant, foreignClearanceId, other);
    }

    // The member (the reader) holds a clearance at the given level; a cleared owner uploaded an item at HighLevelPriority
    private sealed record AccessScenario(GrantScenario Grant, HttpClient Reader, Guid ClearanceId, Guid ItemId);

    private async Task<AccessScenario> ArrangeAccessAsync(string prefix, string readerLevel, int readerPriority)
    {
        var grant = await CreateGrantScenarioAsync(factory, Prefix(prefix));
        var clearanceId = await CreateClearanceAsync(
            grant.OrgAdmin, grant.OrganizationId, grant.MemberId, grant.PolicyId, readerLevel, readerPriority);
        var (owner, ownerId) = await AddUserWithRoleAsync(factory, grant.Organization, Prefix($"{prefix}_owner"), MemberRole);
        await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, ownerId, grant.PolicyId, OwnerLevelName, OwnerLevelPriority);
        return new AccessScenario(grant, grant.Member, clearanceId, await UploadClassifiedItemAsync(owner, grant.PolicyId));
    }

    private static async Task<Guid> UploadClassifiedItemAsync(HttpClient owner, Guid policyId)
    {
        var file = new ByteArrayContent("classified"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var form = new MultipartFormDataContent
        {
            { file, "file", "classified.txt" },
            { new StringContent(policyId.ToString()), "policyId" },
            { new StringContent(HighLevelPriority.ToString()), "levelPriority" }
        };
        var response = await owner.PostAsync(UploadEndpoint, form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AddDataItemResponse>())!.DataItemId;
    }

    private static async Task<List<DataItemSummary>> ListItemsAsync(HttpClient reader) =>
        await reader.GetFromJsonAsync<List<DataItemSummary>>(ListEndpoint) ?? [];

    private static Task<HttpResponseMessage> UpdateAsync(
        HttpClient caller, Guid organizationId, Guid clearanceId, string? name, string? levelName, int priority) =>
        caller.PutAsJsonAsync(ClearanceEndpoint(organizationId, clearanceId), new
        {
            Name = name,
            ClassificationLevelName = levelName,
            ClassificationLevelPriority = priority
        });

    private static async Task<ClearanceResponse> GetClearanceAsync(GrantScenario grant, Guid clearanceId) =>
        (await grant.OrgAdmin.GetFromJsonAsync<ClearanceResponse>(ClearanceEndpoint(grant.OrganizationId, clearanceId)))!;

    private static (string, int) NameAndPriority(ClearanceResponse clearance) => (clearance.Name, clearance.ClassificationLevelPriority);

    private static (string, int) LevelOf(ClearanceResponse clearance) =>
        (clearance.ClassificationLevelName, clearance.ClassificationLevelPriority);

    private async Task<List<DbClassificationLevel>> LevelRowsAsync(string name, int priority) =>
        (await AllLevelRowsAsync()).Where(l => l.Name == name && l.Priority == priority).ToList();

    private async Task<List<DbClassificationLevel>> AllLevelRowsAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().ClassificationLevels.AsNoTracking().ToListAsync();
    }

    private async Task<int> LevelIdOfAsync(Guid clearanceId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().Clearances.AsNoTracking()
            .Where(c => c.Id == clearanceId).Select(c => c.ClassificationLevel.Id).SingleAsync();
    }

    private static Task AssertCannotChangeOwnAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(response, HttpStatusCode.Conflict,
            AdminErrorCodes.ClearanceCannotChangeOwn, AdminErrorMessages.ClearanceCannotChangeOwn);

    private static Task AssertOrganizationNotFoundAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(response, HttpStatusCode.NotFound,
            AdminErrorCodes.OrganizationNotFound, AdminErrorMessages.OrganizationNotFound);

    private static Task AssertClearanceNotFoundAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(response, HttpStatusCode.NotFound,
            AdminErrorCodes.ClearanceNotFound, AdminErrorMessages.ClearanceNotFound);

    private static Task AssertValidationErrorAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(response, HttpStatusCode.BadRequest,
            AdminErrorCodes.ValidationGeneral, AdminErrorMessages.ValidationGeneral);

    // The audit records a request added: the trail before it, compared with the trail after it
    private async Task<List<DbAuditRecord>> RecordsAddedByAsync(Func<Task<HttpResponseMessage>> request)
    {
        var before = (await StoredRecordsAsync()).Select(r => r.Id).ToHashSet();
        await request();
        return (await StoredRecordsAsync()).Where(r => !before.Contains(r.Id)).ToList();
    }

    private async Task<List<DbAuditRecord>> StoredRecordsAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().AuditRecords.AsNoTracking().ToListAsync();
    }

    private record ClearanceResponse(
        Guid Id, string Name, Guid UserId, Guid PolicyId, string ClassificationLevelName, int ClassificationLevelPriority);
    private record AddDataItemResponse(Guid DataItemId);
    private record DataItemSummary(Guid Id);
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.DataManagement;

// The delete rule D5' (ADR-0014, #95 step 4b) through HTTP: a classified item is deleted by its owner while they are
// a member of its organization, or by an organization admin of its organization, even one who cannot read it; a
// personal item by its owner only; anyone else gets 404. GET /data and GET /data/{id} report it as canDelete.
// Deleting an account soft-deletes only the user's personal items (U5'). Runs on SQLite, so the canDelete
// subquery goes through real SQL translation.
public class DeleteRuleTests(SqliteApiFactory factory) : IClassFixture<SqliteApiFactory>
{
    private const string UploadEndpoint = "/data/upload";
    private const string ListEndpoint = "/data";
    private const string AccountsEndpoint = "/admin/accounts";
    private const string NotFoundCode = "DataItem.NotFound";
    private const string ClearanceLevelName = "Secret";
    private const int ClearancePriority = 3;
    private const int ItemPriority = 2;

    // --- DELETE /data/{id} for each role ---

    [Fact]
    public async Task Delete_OwnerWhoIsMember_Returns204AndMarksDeleted()
    {
        var setup = await ArrangeAsync("d5_owner");

        var response = await setup.Owner.DeleteAsync(ItemEndpoint(setup.ItemId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(await IsMarkedDeletedAsync(setup.ItemId));
    }

    [Fact]
    public async Task Delete_OwnerWhoIsGuest_Returns204()
    {
        var setup = await ArrangeAsync("d5_guest");
        var (guest, guestId) = await CreateUserAsync(factory, "d5_guest_uploader");
        await AddMemberAsync(setup.Org.OrgAdmin, setup.OrganizationId, guestId, GuestRole);
        await CreateClearanceAsync(setup.Org.OrgAdmin, setup.OrganizationId, guestId, setup.PolicyId, ClearanceLevelName, ClearancePriority);
        var itemId = await UploadClassifiedAsync(guest, setup.PolicyId);

        var response = await guest.DeleteAsync(ItemEndpoint(itemId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OwnerWhoLeftTheOrganization_Returns404AndKeepsItem()
    {
        var setup = await ArrangeAsync("d5_left");
        (await LeaveOrganizationAsync(setup.Owner, setup.OrganizationId)).EnsureSuccessStatusCode();

        var response = await setup.Owner.DeleteAsync(ItemEndpoint(setup.ItemId));

        await AssertNotFoundAsync(response);
        Assert.False(await IsMarkedDeletedAsync(setup.ItemId));
    }

    [Fact]
    public async Task Delete_OwnerRemovedFromTheOrganization_Returns404()
    {
        var setup = await ArrangeAsync("d5_removed");
        (await DeleteMemberAsync(setup.Org.OrgAdmin, setup.OrganizationId, setup.OwnerId)).EnsureSuccessStatusCode();

        var response = await setup.Owner.DeleteAsync(ItemEndpoint(setup.ItemId));

        await AssertNotFoundAsync(response);
    }

    // The owner is still a member: losing the clearance hides the item (D3') but does not take the delete right
    [Fact]
    public async Task Delete_OwnerWhoIsMemberWithoutClearance_Returns204()
    {
        var setup = await ArrangeAsync("d5_unclear_owner");
        (await setup.Org.OrgAdmin.DeleteAsync(ClearanceEndpoint(setup.OrganizationId, setup.OwnerClearanceId)))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await setup.Owner.GetAsync(ItemEndpoint(setup.ItemId))).StatusCode);

        var response = await setup.Owner.DeleteAsync(ItemEndpoint(setup.ItemId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OrgAdminWhoIsNotOwner_Returns204AndMarksDeleted()
    {
        var setup = await ArrangeAsync("d5_orgadmin");
        await GrantOrgAdminClearanceAsync(setup);

        var response = await setup.Org.OrgAdmin.DeleteAsync(ItemEndpoint(setup.ItemId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(await IsMarkedDeletedAsync(setup.ItemId));
    }

    // AC4: the organization admin without a clearance does not see the item, but may delete it by id
    [Fact]
    public async Task Delete_OrgAdminWithoutClearance_CannotSeeItemButReturns204()
    {
        var setup = await ArrangeAsync("d5_orgadmin_blind");

        var listed = (await setup.Org.OrgAdmin.GetFromJsonAsync<List<SummaryResponse>>(ListEndpoint))!;
        var byId = await setup.Org.OrgAdmin.GetAsync(ItemEndpoint(setup.ItemId));
        var response = await setup.Org.OrgAdmin.DeleteAsync(ItemEndpoint(setup.ItemId));

        Assert.DoesNotContain(listed, item => item.Id == setup.ItemId);
        Assert.Equal(HttpStatusCode.NotFound, byId.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(await IsMarkedDeletedAsync(setup.ItemId));
    }

    [Fact]
    public async Task Delete_OrgAdminOfAnotherOrganization_Returns404()
    {
        var setup = await ArrangeAsync("d5_orgadmin_else");
        var elsewhere = await CreateOrganizationWithOrgAdminAsync(factory, "d5_orgadmin_else_other");

        var response = await elsewhere.OrgAdmin.DeleteAsync(ItemEndpoint(setup.ItemId));

        await AssertNotFoundAsync(response);
        Assert.False(await IsMarkedDeletedAsync(setup.ItemId));
    }

    // The member can read the item (cleared above its level) but is not its owner
    [Fact]
    public async Task Delete_ClearedMemberWhoIsNotOwner_Returns404AndKeepsItem()
    {
        var setup = await ArrangeAsync("d5_member");
        var (reader, _) = await CreateClearedMemberAsync(setup, "d5_member_reader");
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(ItemEndpoint(setup.ItemId))).StatusCode);

        var response = await reader.DeleteAsync(ItemEndpoint(setup.ItemId));

        await AssertNotFoundAsync(response);
        Assert.False(await IsMarkedDeletedAsync(setup.ItemId));
    }

    [Fact]
    public async Task Delete_GuestWhoIsNotOwner_Returns404()
    {
        var setup = await ArrangeAsync("d5_guest_other");
        var (guest, guestId) = await CreateUserAsync(factory, "d5_guest_other_user");
        await AddMemberAsync(setup.Org.OrgAdmin, setup.OrganizationId, guestId, GuestRole);

        var response = await guest.DeleteAsync(ItemEndpoint(setup.ItemId));

        await AssertNotFoundAsync(response);
    }

    [Fact]
    public async Task Delete_Outsider_Returns404AndKeepsItem()
    {
        var setup = await ArrangeAsync("d5_outsider");
        var (outsider, _) = await CreateUserAsync(factory, "d5_outsider_user");

        var response = await outsider.DeleteAsync(ItemEndpoint(setup.ItemId));

        await AssertNotFoundAsync(response);
        Assert.False(await IsMarkedDeletedAsync(setup.ItemId));
    }

    [Fact]
    public async Task Delete_SecondDeleteByOrgAdminAfterOwner_Returns404()
    {
        var setup = await ArrangeAsync("d5_twice");
        Assert.Equal(HttpStatusCode.NoContent, (await setup.Owner.DeleteAsync(ItemEndpoint(setup.ItemId))).StatusCode);

        var byOwner = await setup.Owner.DeleteAsync(ItemEndpoint(setup.ItemId));
        var byOrgAdmin = await setup.Org.OrgAdmin.DeleteAsync(ItemEndpoint(setup.ItemId));

        await AssertNotFoundAsync(byOwner);
        await AssertNotFoundAsync(byOrgAdmin);
    }

    [Fact]
    public async Task Delete_PersonalItemOfMemberByOrgAdmin_Returns404()
    {
        var setup = await ArrangeAsync("d5_personal");
        var personalId = await UploadPersonalAsync(setup.Owner);

        var response = await setup.Org.OrgAdmin.DeleteAsync(ItemEndpoint(personalId));

        await AssertNotFoundAsync(response);
        Assert.False(await IsMarkedDeletedAsync(personalId));
    }

    // AC2: a classified item whose policy no longer exists has no organization; only its owner may delete it
    [Fact]
    public async Task Delete_PolicyGone_OrgAdminReturns404()
    {
        var setup = await ArrangeAsync("d5_gone_admin");
        await RemovePolicyRowAsync(setup.PolicyId);

        var response = await setup.Org.OrgAdmin.DeleteAsync(ItemEndpoint(setup.ItemId));

        await AssertNotFoundAsync(response);
        Assert.False(await IsMarkedDeletedAsync(setup.ItemId));
    }

    [Fact]
    public async Task Delete_PolicyGone_OwnerReturns204()
    {
        var setup = await ArrangeAsync("d5_gone_owner");
        await RemovePolicyRowAsync(setup.PolicyId);

        var response = await setup.Owner.DeleteAsync(ItemEndpoint(setup.ItemId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // --- canDelete in GET /data and GET /data/{id}, matching what DELETE then does ---

    [Fact]
    public async Task CanDelete_OwnerAndClearedOrgAdmin_TrueInListAndById_ThenDeleteReturns204()
    {
        var setup = await ArrangeAsync("d5_flag_true");
        await GrantOrgAdminClearanceAsync(setup);

        Assert.Equal((true, true), await CanDeleteFlagsAsync(setup.Owner, setup.ItemId));
        Assert.Equal((true, true), await CanDeleteFlagsAsync(setup.Org.OrgAdmin, setup.ItemId));
        Assert.Equal(HttpStatusCode.NoContent, (await setup.Org.OrgAdmin.DeleteAsync(ItemEndpoint(setup.ItemId))).StatusCode);
    }

    [Fact]
    public async Task CanDelete_ClearedMemberWhoIsNotOwner_FalseInListAndById_ThenDeleteReturns404()
    {
        var setup = await ArrangeAsync("d5_flag_false");
        var (reader, _) = await CreateClearedMemberAsync(setup, "d5_flag_false_reader");

        Assert.Equal((false, false), await CanDeleteFlagsAsync(reader, setup.ItemId));
        await AssertNotFoundAsync(await reader.DeleteAsync(ItemEndpoint(setup.ItemId)));
    }

    // The owner of a classified item who left keeps no clearance there (C7), so the item is not listed for them;
    // a reader who stays sees it, and the owner's leaving does not make it deletable for that reader
    [Fact]
    public async Task CanDelete_OwnerLeft_FalseForClearedReader_AndOwnerDeleteReturns404()
    {
        var setup = await ArrangeAsync("d5_flag_left");
        var (reader, _) = await CreateClearedMemberAsync(setup, "d5_flag_left_reader");
        (await LeaveOrganizationAsync(setup.Owner, setup.OrganizationId)).EnsureSuccessStatusCode();

        Assert.Equal((false, false), await CanDeleteFlagsAsync(reader, setup.ItemId));
        await AssertNotFoundAsync(await setup.Owner.DeleteAsync(ItemEndpoint(setup.ItemId)));
    }

    [Fact]
    public async Task CanDelete_PersonalItemOfOwner_TrueAndIsOwnerTrue()
    {
        var owner = await TestUsers.CreateClientAsync(factory, "d5_flag_personal@example.com");
        var itemId = await UploadPersonalAsync(owner);

        var listed = Assert.Single((await owner.GetFromJsonAsync<List<SummaryResponse>>(ListEndpoint))!);
        var byId = (await owner.GetFromJsonAsync<DetailResponse>(ItemEndpoint(itemId)))!;

        Assert.Equal((itemId, true, true), (listed.Id, listed.IsOwner, listed.CanDelete));
        Assert.Equal((true, true), (byId.IsOwner, byId.CanDelete));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(ItemEndpoint(itemId))).StatusCode);
    }

    // isOwner is kept beside canDelete: the organization admin may delete but did not upload
    [Fact]
    public async Task CanDelete_ClearedOrgAdmin_IsOwnerFalse()
    {
        var setup = await ArrangeAsync("d5_flag_isowner");
        await GrantOrgAdminClearanceAsync(setup);

        var listed = (await setup.Org.OrgAdmin.GetFromJsonAsync<List<SummaryResponse>>(ListEndpoint))!
            .Single(item => item.Id == setup.ItemId);
        var byId = (await setup.Org.OrgAdmin.GetFromJsonAsync<DetailResponse>(ItemEndpoint(setup.ItemId)))!;

        Assert.Equal((false, true), (listed.IsOwner, listed.CanDelete));
        Assert.Equal((false, true), (byId.IsOwner, byId.CanDelete));
    }

    // --- deleting an account (U5') ---

    [Fact]
    public async Task DeleteAccount_SoftDeletesPersonalItemsOnly()
    {
        var setup = await ArrangeAsync("d5_account_items");
        var personalId = await UploadPersonalAsync(setup.Owner);

        (await setup.Org.PlatformAdmin.DeleteAsync($"{AccountsEndpoint}/{setup.OwnerId}")).EnsureSuccessStatusCode();

        Assert.True(await IsMarkedDeletedAsync(personalId));
        Assert.False(await IsMarkedDeletedAsync(setup.ItemId));
    }

    [Fact]
    public async Task DeleteAccount_ClassifiedItemStaysVisibleToClearedReader()
    {
        var setup = await ArrangeAsync("d5_account_reader");
        var (reader, _) = await CreateClearedMemberAsync(setup, "d5_account_reader_reader");

        (await setup.Org.PlatformAdmin.DeleteAsync($"{AccountsEndpoint}/{setup.OwnerId}")).EnsureSuccessStatusCode();

        var listed = (await reader.GetFromJsonAsync<List<SummaryResponse>>(ListEndpoint))!;
        Assert.Contains(listed, item => item.Id == setup.ItemId && !item.CanDelete);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(ItemEndpoint(setup.ItemId))).StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_ClassifiedItemDeletableByOrgAdmin()
    {
        var setup = await ArrangeAsync("d5_account_orgadmin");

        (await setup.Org.PlatformAdmin.DeleteAsync($"{AccountsEndpoint}/{setup.OwnerId}")).EnsureSuccessStatusCode();
        var response = await setup.Org.OrgAdmin.DeleteAsync(ItemEndpoint(setup.ItemId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(await IsMarkedDeletedAsync(setup.ItemId));
    }

    // --- helpers ---

    // An organization with its organization admin and a policy; an owner, a member cleared above the item's level,
    // uploads one classified item under the policy
    private sealed record Setup(OrganizationScenario Org, Guid PolicyId, HttpClient Owner, Guid OwnerId, Guid OwnerClearanceId, Guid ItemId)
    {
        internal Guid OrganizationId => Org.OrganizationId;
    }

    private async Task<Setup> ArrangeAsync(string prefix)
    {
        var organization = await CreateOrganizationWithOrgAdminAsync(factory, prefix);
        var policyId = await CreatePolicyAsync(organization.OrgAdmin, $"{prefix} policy", organization.OrganizationId);
        var (owner, ownerId) = await CreateUserAsync(factory, $"{prefix}_owner");
        var ownerClearanceId = await AddMemberAndGrantClearanceAsync(
            organization.OrgAdmin, organization.OrganizationId, ownerId, policyId, ClearanceLevelName, ClearancePriority);
        var itemId = await UploadClassifiedAsync(owner, policyId);
        return new Setup(organization, policyId, owner, ownerId, ownerClearanceId, itemId);
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateClearedMemberAsync(Setup setup, string name)
    {
        var (client, userId) = await CreateUserAsync(factory, name);
        await AddMemberAndGrantClearanceAsync(
            setup.Org.OrgAdmin, setup.OrganizationId, userId, setup.PolicyId, ClearanceLevelName, ClearancePriority);
        return (client, userId);
    }

    // C8 forbids granting oneself a clearance, so the organization admin's clearance (at the item's level) is seeded
    private Task GrantOrgAdminClearanceAsync(Setup setup) => SeedClearanceAsync(factory, setup.Org.OrgAdminId, setup.PolicyId);

    // canDelete as GET /data and GET /data/{id} report it for the caller
    private static async Task<(bool InList, bool ById)> CanDeleteFlagsAsync(HttpClient caller, Guid itemId)
    {
        var listed = (await caller.GetFromJsonAsync<List<SummaryResponse>>(ListEndpoint))!.Single(item => item.Id == itemId);
        var byId = (await caller.GetFromJsonAsync<DetailResponse>(ItemEndpoint(itemId)))!;
        return (listed.CanDelete, byId.CanDelete);
    }

    private static string ItemEndpoint(Guid itemId) => $"/data/{itemId}";

    private static async Task AssertNotFoundAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFoundCode, (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Code);
    }

    // Stands for the known gap "deleting a policy ignores its data items": the policy row goes, the items stay
    private async Task RemovePolicyRowAsync(Guid policyId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        db.Policies.Remove(await db.Policies.SingleAsync(policy => policy.Id == policyId));
        await db.SaveChangesAsync();
    }

    private async Task<bool> IsMarkedDeletedAsync(Guid itemId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        return (await db.DataItems.AsNoTracking().SingleAsync(item => item.Id == itemId)).IsDeleted;
    }

    private static async Task<Guid> UploadPersonalAsync(HttpClient client)
    {
        var file = new ByteArrayContent("personal"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var response = await client.PostAsync(UploadEndpoint, new MultipartFormDataContent { { file, "file", "notes.txt" } });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AddDataItemResponse>())!.DataItemId;
    }

    private static async Task<Guid> UploadClassifiedAsync(HttpClient client, Guid policyId)
    {
        var file = new ByteArrayContent("classified"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var form = new MultipartFormDataContent
        {
            { file, "file", "report.txt" },
            { new StringContent(policyId.ToString()), "policyId" },
            { new StringContent(ItemPriority.ToString()), "levelPriority" }
        };
        var response = await client.PostAsync(UploadEndpoint, form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AddDataItemResponse>())!.DataItemId;
    }

    private record SummaryResponse(Guid Id, bool IsOwner, bool CanDelete);
    private record DetailResponse(Guid Id, bool IsOwner, bool CanDelete);
    private record AddDataItemResponse(Guid DataItemId);
    private record ErrorResponse(string Code, string Description);
}

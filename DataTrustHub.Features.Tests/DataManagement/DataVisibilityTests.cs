using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.DataManagement;

// The read rule D3' (ADR-0014, #95 step 4a): a classified item is read only through a clearance on its policy at least
// at its level, the owner included; a personal item only by its owner. Clearances are granted and revoked by an
// organization admin of the policy's organization (#95 step 3b)
public class DataVisibilityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string UploadEndpoint = "/data/upload";
    private const string ListEndpoint = "/data";
    private const string PersonalMarking = "UNCLASSIFIED";
    private const string InternalName = "Internal";
    private const string ConfidentialName = "Confidential";
    private const int Internal = 1;
    private const int Confidential = 2;
    private const int Secret = 3;

    [Fact]
    public async Task ClearedAtOrAboveLevel_SeesItem_InListAndById()
    {
        var setup = await ArrangeAsync("vis_above");
        var reader = await CreateClearedUserAsync(setup, "vis_above_reader", Secret);

        var listed = await reader.GetFromJsonAsync<List<SummaryResponse>>(ListEndpoint);
        var byId = await reader.GetAsync($"/data/{setup.ItemId}");

        var item = Assert.Single(listed!);
        Assert.Equal((setup.ItemId, "Confidential", false), (item.Id, item.SecurityMarking, item.IsOwner));
        Assert.Equal("vis_above_owner@example.com", item.OwnerEmail);
        Assert.Equal(HttpStatusCode.OK, byId.StatusCode);
    }

    [Fact]
    public async Task ClearedBelowLevel_DoesNotSeeItem()
    {
        var setup = await ArrangeAsync("vis_below");
        var reader = await CreateClearedUserAsync(setup, "vis_below_reader", Internal);

        Assert.Empty((await reader.GetFromJsonAsync<List<SummaryResponse>>(ListEndpoint))!);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/data/{setup.ItemId}")).StatusCode);
    }

    [Fact]
    public async Task ClearedOnAnotherPolicy_DoesNotSeeItem()
    {
        var setup = await ArrangeAsync("vis_other");
        var (otherOrganizationId, otherOrgAdmin, otherPolicy) =
            await AdminSetup.CreatePolicyWithOrgAdminAsync(factory, setup.Admin, "vis_other second policy");
        var reader = await CreateClearedUserAsync(
            setup with { OrganizationId = otherOrganizationId, OrgAdmin = otherOrgAdmin, PolicyId = otherPolicy }, "vis_other_reader", Secret);

        Assert.Empty((await reader.GetFromJsonAsync<List<SummaryResponse>>(ListEndpoint))!);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/data/{setup.ItemId}")).StatusCode);
    }

    [Fact]
    public async Task AdminWithoutClearance_DoesNotSeeItem()
    {
        var setup = await ArrangeAsync("vis_admin");

        Assert.Empty((await setup.Admin.GetFromJsonAsync<List<SummaryResponse>>(ListEndpoint))!);
        Assert.Equal(HttpStatusCode.NotFound, (await setup.Admin.GetAsync($"/data/{setup.ItemId}")).StatusCode);
    }

    [Fact]
    public async Task Owner_SeesOwnItemMarkedAsOwner_AndDeletedItemDisappearsForReaders()
    {
        var setup = await ArrangeAsync("vis_owner");
        var reader = await CreateClearedUserAsync(setup, "vis_owner_reader", Secret);

        var own = Assert.Single((await setup.Owner.GetFromJsonAsync<List<SummaryResponse>>(ListEndpoint))!);
        await setup.Owner.DeleteAsync($"/data/{setup.ItemId}");

        Assert.True(own.IsOwner);
        Assert.Empty((await reader.GetFromJsonAsync<List<SummaryResponse>>(ListEndpoint))!);
    }

    [Fact]
    public async Task OwnerWhoseClearanceIsRevoked_NoLongerSeesClassifiedItem()
    {
        var setup = await ArrangeAsync("vis_revoked");

        await RevokeOwnerClearanceAsync(setup);

        Assert.Empty((await setup.Owner.GetFromJsonAsync<List<SummaryResponse>>(ListEndpoint))!);
        Assert.Equal(HttpStatusCode.NotFound, (await setup.Owner.GetAsync($"/data/{setup.ItemId}")).StatusCode);
    }

    [Fact]
    public async Task OwnerWhoseClearanceIsRevoked_SeesClassifiedItemAgainAfterNewGrant()
    {
        var setup = await ArrangeAsync("vis_regrant");
        await RevokeOwnerClearanceAsync(setup);

        await AdminSetup.CreateClearanceAsync(
            setup.OrgAdmin, setup.OrganizationId, setup.OwnerId, setup.PolicyId, ConfidentialName, Confidential);

        var own = Assert.Single((await setup.Owner.GetFromJsonAsync<List<SummaryResponse>>(ListEndpoint))!);
        Assert.Equal((setup.ItemId, true), (own.Id, own.IsOwner));
        Assert.Equal(HttpStatusCode.OK, (await setup.Owner.GetAsync($"/data/{setup.ItemId}")).StatusCode);
    }

    [Fact]
    public async Task OwnerWithClearanceLoweredBelowLevel_DoesNotSeeClassifiedItem()
    {
        var setup = await ArrangeAsync("vis_lowered");
        await RevokeOwnerClearanceAsync(setup);

        await AdminSetup.CreateClearanceAsync(
            setup.OrgAdmin, setup.OrganizationId, setup.OwnerId, setup.PolicyId, InternalName, Internal);

        Assert.Empty((await setup.Owner.GetFromJsonAsync<List<SummaryResponse>>(ListEndpoint))!);
        Assert.Equal(HttpStatusCode.NotFound, (await setup.Owner.GetAsync($"/data/{setup.ItemId}")).StatusCode);
    }

    [Fact]
    public async Task OwnerWhoseClearanceIsRevoked_ItemStaysVisibleToClearedReader()
    {
        var setup = await ArrangeAsync("vis_revoked_reader");
        var reader = await CreateClearedUserAsync(setup, "vis_revoked_reader_reader", Secret);

        await RevokeOwnerClearanceAsync(setup);

        var item = Assert.Single((await reader.GetFromJsonAsync<List<SummaryResponse>>(ListEndpoint))!);
        Assert.Equal((setup.ItemId, false), (item.Id, item.IsOwner));
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"/data/{setup.ItemId}")).StatusCode);
    }

    [Fact]
    public async Task PersonalItem_OwnerWithoutClearance_SeesIt()
    {
        var owner = await TestUsers.CreateClientAsync(factory, "vis_personal_owner@example.com");
        var itemId = await UploadPersonalAsync(owner);

        var own = Assert.Single((await owner.GetFromJsonAsync<List<SummaryResponse>>(ListEndpoint))!);
        Assert.Equal((itemId, PersonalMarking, true), (own.Id, own.SecurityMarking, own.IsOwner));
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/data/{itemId}")).StatusCode);
    }

    [Fact]
    public async Task PersonalItem_NonOwnerWithClearance_DoesNotSeeIt()
    {
        var setup = await ArrangeAsync("vis_personal_other");
        var itemId = await UploadPersonalAsync(setup.Owner);
        var reader = await CreateClearedUserAsync(setup, "vis_personal_other_reader", Secret);

        var listed = (await reader.GetFromJsonAsync<List<SummaryResponse>>(ListEndpoint))!;

        Assert.DoesNotContain(listed, item => item.Id == itemId);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/data/{itemId}")).StatusCode);
    }

    // --- helpers ---

    // Admin is the platform admin; OrgAdmin is the organization admin of the policy's organization
    // OwnerId and OwnerClearanceId identify the owner and the clearance they uploaded with
    private sealed record Setup(
        HttpClient Admin, Guid OrganizationId, HttpClient OrgAdmin, HttpClient Owner, Guid PolicyId, Guid ItemId,
        Guid OwnerId = default, Guid OwnerClearanceId = default);

    // An owner cleared Secret on a policy uploads one Confidential item under it
    private async Task<Setup> ArrangeAsync(string prefix)
    {
        var admin = await TestUsers.CreateClientAsync(factory, $"{prefix}_admin@example.com", UserRoles.Admin);
        var (organizationId, orgAdmin, policyId) = await AdminSetup.CreatePolicyWithOrgAdminAsync(factory, admin, $"{prefix} policy");
        var (owner, ownerId, ownerClearanceId) = await CreateClearedUserWithIdsAsync(
            new Setup(admin, organizationId, orgAdmin, admin, policyId, Guid.Empty), $"{prefix}_owner", Secret);
        var itemId = await UploadAsync(owner, policyId, Confidential);
        return new Setup(admin, organizationId, orgAdmin, owner, policyId, itemId, ownerId, ownerClearanceId);
    }

    private async Task<HttpClient> CreateClearedUserAsync(Setup setup, string name, int priority) =>
        (await CreateClearedUserWithIdsAsync(setup, name, priority)).Client;

    // The organization admin adds the user as a member and grants the clearance (C5, C6)
    private async Task<(HttpClient Client, Guid UserId, Guid ClearanceId)> CreateClearedUserWithIdsAsync(
        Setup setup, string name, int priority)
    {
        var email = $"{name}@example.com";
        var client = await TestUsers.CreateClientAsync(factory, email);
        var userId = await TestUsers.GetIdAsync(factory, email);
        var clearanceId = await AdminSetup.AddMemberAndGrantClearanceAsync(
            setup.OrgAdmin, setup.OrganizationId, userId, setup.PolicyId, $"Level {priority}", priority);
        return (client, userId, clearanceId);
    }

    // The organization admin revokes the owner's clearance through the organization-scoped route
    private static async Task RevokeOwnerClearanceAsync(Setup setup)
    {
        var route = AdminSetup.ClearanceEndpoint(setup.OrganizationId, setup.OwnerClearanceId);
        (await setup.OrgAdmin.DeleteAsync(route)).EnsureSuccessStatusCode();
    }

    private static async Task<Guid> UploadPersonalAsync(HttpClient client)
    {
        var file = new ByteArrayContent("personal"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var response = await client.PostAsync(UploadEndpoint, new MultipartFormDataContent { { file, "file", "notes.txt" } });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AddDataItemResponse>())!.DataItemId;
    }

    private static async Task<Guid> UploadAsync(HttpClient client, Guid policyId, int levelPriority)
    {
        var file = new ByteArrayContent("classified"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var form = new MultipartFormDataContent
        {
            { file, "file", "report.txt" },
            { new StringContent(policyId.ToString()), "policyId" },
            { new StringContent(levelPriority.ToString()), "levelPriority" }
        };
        var response = await client.PostAsync(UploadEndpoint, form);
        return (await response.Content.ReadFromJsonAsync<AddDataItemResponse>())!.DataItemId;
    }

    private record SummaryResponse(Guid Id, string SecurityMarking, string? OwnerEmail, bool IsOwner);
    private record AddDataItemResponse(Guid DataItemId);
}

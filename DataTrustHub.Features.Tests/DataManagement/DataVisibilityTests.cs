using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.DataManagement;

// The read rule of #5: owner, or a clearance on the item's policy at least at the item's level. Since #95 step 3b the
// clearances are granted by an organization admin of the policy's organization to a member (AC9: the rule is unchanged)
public class DataVisibilityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string UploadEndpoint = "/data/upload";
    private const string ListEndpoint = "/data";
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

    // --- helpers ---

    // Admin is the platform admin; OrgAdmin is the organization admin of the policy's organization
    private sealed record Setup(HttpClient Admin, Guid OrganizationId, HttpClient OrgAdmin, HttpClient Owner, Guid PolicyId, Guid ItemId);

    // An owner cleared Secret on a policy uploads one Confidential item under it
    private async Task<Setup> ArrangeAsync(string prefix)
    {
        var admin = await TestUsers.CreateClientAsync(factory, $"{prefix}_admin@example.com", UserRoles.Admin);
        var (organizationId, orgAdmin, policyId) = await AdminSetup.CreatePolicyWithOrgAdminAsync(factory, admin, $"{prefix} policy");
        var owner = await CreateClearedUserAsync(
            new Setup(admin, organizationId, orgAdmin, admin, policyId, Guid.Empty), $"{prefix}_owner", Secret);
        var itemId = await UploadAsync(owner, policyId, Confidential);
        return new Setup(admin, organizationId, orgAdmin, owner, policyId, itemId);
    }

    // The organization admin adds the user as a member and grants the clearance (C5, C6)
    private async Task<HttpClient> CreateClearedUserAsync(Setup setup, string name, int priority)
    {
        var email = $"{name}@example.com";
        var client = await TestUsers.CreateClientAsync(factory, email);
        var userId = await TestUsers.GetIdAsync(factory, email);
        await AdminSetup.AddMemberAndGrantClearanceAsync(
            setup.OrgAdmin, setup.OrganizationId, userId, setup.PolicyId, $"Level {priority}", priority);
        return client;
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

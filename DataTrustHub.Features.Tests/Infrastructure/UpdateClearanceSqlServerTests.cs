using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.ClearanceSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #23 (UpdateClearance) on SQL Server itself (a LocalDB database built by the migrations; skipped where LocalDB is
// missing): the repository's UpdateAsync stores the new name and level (AC1), the read rule on SQL sees the new level
// right away, raised or lowered (AC2), and a stored level with the same name and priority is reused while other
// clearances keep the old one (AC7)
public class UpdateClearanceSqlServerTests(LocalDbApiFactory factory) : IClassFixture<LocalDbApiFactory>
{
    private const string NewName = "Changed clearance";
    private const string UploadEndpoint = "/data/upload";
    private const string ListEndpoint = "/data";
    private const string OwnerLevelName = "Top Secret";
    private const int OwnerLevelPriority = 4;
    private const string LowLevelName = "Internal";
    private const int LowLevelPriority = 1;
    private const string HighLevelName = "Secret";
    private const int HighLevelPriority = 3;
    private const int CustomPriority = 7;

    [LocalDbFact]
    public async Task UpdateClearance_NewNameAndLevel_StoresThem()
    {
        var (grant, clearanceId) = await ArrangeAsync("sql_update_clearance");
        var levelName = UniqueLevelName("Stored");

        var response = await UpdateAsync(grant, clearanceId, levelName, CustomPriority);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            new ClearanceResponse(clearanceId, NewName, grant.MemberId, grant.PolicyId, levelName, CustomPriority),
            await GetClearanceAsync(grant, clearanceId));
    }

    // --- AC2 ---

    [LocalDbFact]
    public async Task UpdateClearance_RaisedLevel_UserReadsAnItemAtTheNewPriority()
    {
        var access = await ArrangeAccessAsync("sql_update_clearance_raise", LowLevelName, LowLevelPriority);
        Assert.Equal(HttpStatusCode.NotFound, (await access.Reader.GetAsync(ItemEndpoint(access.ItemId))).StatusCode);

        (await UpdateAsync(access.Grant, access.ClearanceId, HighLevelName, HighLevelPriority)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await access.Reader.GetAsync(ItemEndpoint(access.ItemId))).StatusCode);
        Assert.Contains(await ListItemsAsync(access.Reader), i => i.Id == access.ItemId);
    }

    [LocalDbFact]
    public async Task UpdateClearance_LoweredLevel_ItemAboveTheNewPriorityIsNoLongerReadable()
    {
        var access = await ArrangeAccessAsync("sql_update_clearance_lower", HighLevelName, HighLevelPriority);
        Assert.Equal(HttpStatusCode.OK, (await access.Reader.GetAsync(ItemEndpoint(access.ItemId))).StatusCode);

        (await UpdateAsync(access.Grant, access.ClearanceId, LowLevelName, LowLevelPriority)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await access.Reader.GetAsync(ItemEndpoint(access.ItemId))).StatusCode);
        Assert.DoesNotContain(await ListItemsAsync(access.Reader), i => i.Id == access.ItemId);
    }

    // --- AC7 ---

    [LocalDbFact]
    public async Task UpdateClearance_ToAStoredLevel_ReusesItsRow()
    {
        var (grant, clearanceId) = await ArrangeAsync("sql_update_clearance_reuse");
        var storedLevel = UniqueLevelName("Reused");
        var (_, holderId) = await AddUserWithRoleAsync(factory, grant.Organization, Prefix("sql_update_clearance_reuse_holder"), MemberRole);
        var holderClearanceId = await CreateClearanceAsync(
            grant.OrgAdmin, grant.OrganizationId, holderId, grant.PolicyId, storedLevel, CustomPriority);

        (await UpdateAsync(grant, clearanceId, storedLevel, CustomPriority)).EnsureSuccessStatusCode();

        Assert.Equal(1, await CountLevelRowsAsync(storedLevel, CustomPriority));
        Assert.Equal(await LevelIdOfAsync(holderClearanceId), await LevelIdOfAsync(clearanceId));
    }

    [LocalDbFact]
    public async Task UpdateClearance_ToANewLevel_StoresItOnce()
    {
        var (grant, clearanceId) = await ArrangeAsync("sql_update_clearance_new_level");
        var newLevel = UniqueLevelName("New");

        (await UpdateAsync(grant, clearanceId, newLevel, CustomPriority)).EnsureSuccessStatusCode();

        Assert.Equal(1, await CountLevelRowsAsync(newLevel, CustomPriority));
    }

    [LocalDbFact]
    public async Task UpdateClearance_OtherClearanceOnTheOldLevel_KeepsIt()
    {
        var oldLevel = UniqueLevelName("Shared");
        var grant = await CreateGrantScenarioAsync(factory, Prefix("sql_update_clearance_shared"));
        var clearanceId = await CreateClearanceAsync(
            grant.OrgAdmin, grant.OrganizationId, grant.MemberId, grant.PolicyId, oldLevel, CustomPriority);
        var (_, holderId) = await AddUserWithRoleAsync(factory, grant.Organization, Prefix("sql_update_clearance_shared_holder"), MemberRole);
        var otherClearanceId = await CreateClearanceAsync(
            grant.OrgAdmin, grant.OrganizationId, holderId, grant.PolicyId, oldLevel, CustomPriority);
        var oldLevelId = await LevelIdOfAsync(otherClearanceId);

        (await UpdateAsync(grant, clearanceId, HighLevelName, HighLevelPriority)).EnsureSuccessStatusCode();

        var other = await GetClearanceAsync(grant, otherClearanceId);
        Assert.Equal((oldLevel, CustomPriority), (other.ClassificationLevelName, other.ClassificationLevelPriority));
        Assert.Equal((oldLevelId, 1), (await LevelIdOfAsync(otherClearanceId), await CountLevelRowsAsync(oldLevel, CustomPriority)));
    }

    // --- helpers ---

    private static string Prefix(string name) => $"{name}_{Guid.NewGuid():N}";

    private static string UniqueLevelName(string name) => $"{name} level {Guid.NewGuid():N}";

    private static string ItemEndpoint(Guid itemId) => $"{ListEndpoint}/{itemId}";

    private async Task<(GrantScenario Grant, Guid ClearanceId)> ArrangeAsync(string prefix)
    {
        var grant = await CreateGrantScenarioAsync(factory, Prefix(prefix));
        return (grant, await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, grant.MemberId, grant.PolicyId));
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

    private static Task<HttpResponseMessage> UpdateAsync(GrantScenario grant, Guid clearanceId, string levelName, int priority) =>
        grant.OrgAdmin.PutAsJsonAsync(ClearanceEndpoint(grant.OrganizationId, clearanceId), new
        {
            Name = NewName,
            ClassificationLevelName = levelName,
            ClassificationLevelPriority = priority
        });

    private static async Task<ClearanceResponse> GetClearanceAsync(GrantScenario grant, Guid clearanceId) =>
        (await grant.OrgAdmin.GetFromJsonAsync<ClearanceResponse>(ClearanceEndpoint(grant.OrganizationId, clearanceId)))!;

    private async Task<int> CountLevelRowsAsync(string name, int priority)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().ClassificationLevels
            .CountAsync(l => l.Name == name && l.Priority == priority);
    }

    private async Task<int> LevelIdOfAsync(Guid clearanceId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().Clearances.AsNoTracking()
            .Where(c => c.Id == clearanceId).Select(c => c.ClassificationLevel.Id).SingleAsync();
    }

    private record ClearanceResponse(
        Guid Id, string Name, Guid UserId, Guid PolicyId, string ClassificationLevelName, int ClassificationLevelPriority);
    private record AddDataItemResponse(Guid DataItemId);
    private record DataItemSummary(Guid Id);
}

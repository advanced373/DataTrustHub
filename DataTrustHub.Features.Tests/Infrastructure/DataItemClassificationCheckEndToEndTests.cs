using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataTrustHub.Domain.DataItems;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #128 AC4 on SQL Server (a LocalDB database built by the migrations, so CK_DataItems_Classification is in place):
// data items made by the use cases (AddDataItem, through the domain factories) still upload, read, list and
// soft delete with the same status codes. Skipped where LocalDB is missing; the same flows run on EF InMemory in
// the DataManagement tests and the repository round trip runs on SQLite in DataItemClassificationCheckModelTests.
public class DataItemClassificationCheckEndToEndTests(LocalDbApiFactory factory) : IClassFixture<LocalDbApiFactory>
{
    private const string UploadEndpoint = "/data/upload";
    private const string ListEndpoint = "/data";
    private const int Secret = 3;
    private const string SecretMarking = "Secret";

    [LocalDbFact]
    public async Task PersonalDataItem_UploadsReadsListsAndSoftDeletes()
    {
        var user = await TestUsers.CreateClientAsync(factory, "check128_personal@example.com");

        var itemId = await UploadAsync(user, policyId: null, levelPriority: null);

        var item = await user.GetFromJsonAsync<DataItemResponse>(ItemEndpoint(itemId));
        Assert.Equal((DataItem.PersonalMarking, (int?)null), (item!.SecurityMarking, item.ClassificationPriority));
        Assert.Contains(await user.GetFromJsonAsync<List<DataItemSummary>>(ListEndpoint) ?? [], i => i.Id == itemId);
        await AssertSoftDeletedAsync(user, itemId);
        var stored = await StoredAsync(itemId);
        Assert.Equal((true, (Guid?)null, (int?)null), (stored.IsDeleted, stored.PolicyId, stored.ClassificationPriority));
    }

    [LocalDbFact]
    public async Task ClassifiedDataItem_UploadsReadsListsAndSoftDeletes()
    {
        var (user, policyId) = await CreateClearedUserAsync("check128_classified");

        var itemId = await UploadAsync(user, policyId, Secret);

        var item = await user.GetFromJsonAsync<DataItemResponse>(ItemEndpoint(itemId));
        Assert.Equal((SecretMarking, (int?)Secret), (item!.SecurityMarking, item.ClassificationPriority));
        Assert.Contains(await user.GetFromJsonAsync<List<DataItemSummary>>(ListEndpoint) ?? [], i => i.Id == itemId);
        await AssertSoftDeletedAsync(user, itemId);
        var stored = await StoredAsync(itemId);
        Assert.Equal((true, (Guid?)policyId, (int?)Secret), (stored.IsDeleted, stored.PolicyId, stored.ClassificationPriority));
    }

    // --- helpers ---

    private static string ItemEndpoint(Guid id) => $"/data/{id}";

    private static async Task AssertSoftDeletedAsync(HttpClient user, Guid itemId)
    {
        Assert.Equal(HttpStatusCode.NoContent, (await user.DeleteAsync(ItemEndpoint(itemId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync(ItemEndpoint(itemId))).StatusCode);
    }

    private async Task<(HttpClient User, Guid PolicyId)> CreateClearedUserAsync(string prefix)
    {
        var admin = await TestUsers.CreateClientAsync(factory, $"{prefix}_admin@example.com", UserRoles.Admin);
        var user = await TestUsers.CreateClientAsync(factory, $"{prefix}_user@example.com");
        var userId = await TestUsers.GetIdAsync(factory, $"{prefix}_user@example.com");
        var (organizationId, orgAdmin, policyId) = await AdminSetup.CreatePolicyWithOrgAdminAsync(factory, admin, $"{prefix} policy");
        await AdminSetup.AddMemberAndGrantClearanceAsync(orgAdmin, organizationId, userId, policyId, SecretMarking, Secret);
        return (user, policyId);
    }

    private static async Task<Guid> UploadAsync(HttpClient client, Guid? policyId, int? levelPriority)
    {
        var file = new ByteArrayContent("check128"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var form = new MultipartFormDataContent { { file, "file", "check128.txt" } };
        if (policyId is not null) form.Add(new StringContent(policyId.ToString()!), "policyId");
        if (levelPriority is not null) form.Add(new StringContent(levelPriority.ToString()!), "levelPriority");

        var response = await client.PostAsync(UploadEndpoint, form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AddDataItemResponse>())!.DataItemId;
    }

    private async Task<DataTrustHub.Infrastructure.Persistance.Model.DbDataItem> StoredAsync(Guid itemId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().DataItems.AsNoTracking().SingleAsync(d => d.Id == itemId);
    }

    private record AddDataItemResponse(Guid DataItemId);
    private record DataItemResponse(Guid Id, string SecurityMarking, int? ClassificationPriority);
    private record DataItemSummary(Guid Id);
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using DataTrustHub.Domain.Auditing;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Auditing.Helpers;

// A Confidential item on a policy of a new organization, uploaded by an owner cleared Secret there
internal sealed record ClassifiedScenario(
    HttpClient PlatformAdmin, HttpClient OrgAdmin, Guid OrganizationId, Guid PolicyId, HttpClient Owner, Guid OwnerId, Guid ItemId);

/// <summary>
/// Arranges data items through the API and reads the audit trail, for the data-access audit tests (#24 step 2).
/// Works on any ApiFactory: EF InMemory, SQLite or the one with a failing audit log.
/// </summary>
internal static class DataAccessAuditSetup
{
    internal const string DataItemsEndpoint = "/data";
    internal const string ClassificationOptionsEndpoint = "/data/classification-options";
    internal const string DownloadAction = "DataItem.Download";
    internal const string DataItemNotFoundCode = "DataItem.NotFound";
    internal const string FileContent = "audited-download-content";
    internal const int Internal = 1;
    internal const int Confidential = 2;
    internal const int Secret = 3;

    private const string UploadEndpoint = "/data/upload";
    private const string FileName = "download.txt";
    private const string TextPlain = "text/plain";
    private const string FileField = "file";
    private const string PolicyIdField = "policyId";
    private const string LevelPriorityField = "levelPriority";

    internal static readonly string Succeeded = AuditOutcome.Succeeded.ToString();
    internal static readonly string Denied = AuditOutcome.Denied.ToString();

    // The content as the download response carries it
    internal static string Base64Content { get; } = Convert.ToBase64String(Encoding.UTF8.GetBytes(FileContent));

    internal static string ItemEndpoint(Guid itemId) => $"{DataItemsEndpoint}/{itemId}";

    // C5 and D2: the owner is a member cleared Secret on the policy before uploading
    internal static async Task<ClassifiedScenario> ArrangeClassifiedAsync(ApiFactory factory, string prefix)
    {
        var organization = await MembershipSetup.CreateOrganizationWithOrgAdminAsync(factory, prefix);
        var policyId = await AdminSetup.CreatePolicyAsync(organization.OrgAdmin, $"{prefix} policy", organization.OrganizationId);
        var (owner, ownerId) = await MembershipSetup.CreateUserAsync(factory, $"{prefix}_owner");
        await AdminSetup.AddMemberAndGrantClearanceAsync(
            organization.OrgAdmin, organization.OrganizationId, ownerId, policyId, $"Level {Secret}", Secret);
        var itemId = await UploadAsync(owner, policyId, Confidential);
        return new ClassifiedScenario(
            organization.PlatformAdmin, organization.OrgAdmin, organization.OrganizationId, policyId, owner, ownerId, itemId);
    }

    // A member of the scenario's organization cleared on its policy at the given priority
    internal static async Task<(HttpClient Client, Guid UserId)> CreateReaderAsync(
        ApiFactory factory, ClassifiedScenario scenario, string name, int priority)
    {
        var (client, userId) = await MembershipSetup.CreateUserAsync(factory, name);
        await AdminSetup.AddMemberAndGrantClearanceAsync(
            scenario.OrgAdmin, scenario.OrganizationId, userId, scenario.PolicyId, $"Level {priority}", priority);
        return (client, userId);
    }

    internal static async Task<Guid> UploadAsync(HttpClient client, Guid? policyId = null, int? levelPriority = null)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(FileContent));
        file.Headers.ContentType = new MediaTypeHeaderValue(TextPlain);
        var form = new MultipartFormDataContent { { file, FileField, FileName } };
        if (policyId is not null) form.Add(new StringContent(policyId.ToString()!), PolicyIdField);
        if (levelPriority is not null) form.Add(new StringContent(levelPriority.ToString()!), LevelPriorityField);

        var response = await client.PostAsync(UploadEndpoint, form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AddDataItemResponse>())!.DataItemId;
    }

    // The policy row is removed directly: the item keeps its PolicyId, and no policy answers it any more
    internal static async Task RemovePolicyAsync(ApiFactory factory, Guid policyId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        db.Policies.Remove(await db.Policies.SingleAsync(policy => policy.Id == policyId));
        await db.SaveChangesAsync();
    }

    // The audit records a request added: the trail before it, compared with the trail after it. Exact only
    // when the tests of a class run one after another, which xUnit does
    internal static async Task<List<DbAuditRecord>> RecordsAddedByAsync(ApiFactory factory, Func<Task> request)
    {
        var before = (await StoredRecordsAsync(factory)).Select(r => r.Id).ToHashSet();
        await request();
        return (await StoredRecordsAsync(factory)).Where(r => !before.Contains(r.Id)).ToList();
    }

    internal static async Task<List<DbAuditRecord>> StoredRecordsAsync(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().AuditRecords.AsNoTracking().ToListAsync();
    }

    internal static void AssertRecord(
        DbAuditRecord record, Guid actorUserId, Guid itemId, Guid? organizationId, string outcome, string? errorCode)
    {
        Assert.Equal(
            (DownloadAction, (Guid?)actorUserId, (string?)AuditTargetTypes.DataItem, (Guid?)itemId),
            (record.Action, record.ActorUserId, record.TargetType, record.TargetId));
        Assert.Equal((organizationId, outcome, errorCode), (record.OrganizationId, record.Outcome, record.ErrorCode));
    }

    // No text column of the record holds the value (ADR-0016 point 3)
    internal static void AssertHoldsNo(DbAuditRecord record, string value)
    {
        string?[] texts = [record.Action, record.TargetType, record.Outcome, record.ErrorCode];
        Assert.All(texts, text => Assert.DoesNotContain(value, text ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record AddDataItemResponse(Guid DataItemId);
}

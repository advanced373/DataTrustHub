using System.Net.Http.Json;
using System.Text.Json;
using DataTrustHub.Domain.Auditing;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DataTrustHub.Features.Tests.Administration.Helpers;

// An audit record as the two audit trail routes answer it (#24 step 3)
internal sealed record AuditRecordSummaryResponse(
    Guid Id,
    DateTime OccurredAt,
    Guid? ActorUserId,
    string? ActorEmail,
    string Action,
    string? TargetType,
    Guid? TargetId,
    string Outcome,
    string? ErrorCode);

/// <summary>
/// Seeds audit records directly in the test database and reads the two audit trail routes (#24 step 3, ADR-0016).
/// </summary>
internal static class AuditTrailSetup
{
    internal const string PlatformAuditRecordsEndpoint = "/admin/audit-records";

    // The column's length (DbAuditRecord.ActionMaxLength)
    internal const int ActionMaxLength = 128;

    // The answer's limit, newest first (no paging in the MVP)
    internal const int MaxRecords = 200;

    // Tenant-level actions: the platform's own use cases
    internal const string CreateOrganizationAction = "CreateOrganization";
    internal const string DeleteOrganizationAction = "DeleteOrganization";
    internal const string AppointOrganizationAdminAction = "AppointOrganizationAdmin";
    internal const string CreateAccountAction = "CreateAccount";
    internal const string DeleteAccountAction = "DeleteAccount";
    internal const string RegisterUserAction = "RegisterUser";
    internal const string LoginUserAction = "LoginUser";

    internal static readonly string[] TenantLevelActions =
    [
        CreateOrganizationAction, DeleteOrganizationAction, AppointOrganizationAdminAction,
        CreateAccountAction, DeleteAccountAction, RegisterUserAction, LoginUserAction
    ];

    // Actions inside an organization: members, policies, clearances, data items
    internal const string AddMemberAction = "AddMember";
    internal const string CreatePolicyAction = "CreatePolicy";
    internal const string CreateClearanceAction = "CreateClearance";
    internal const string DeleteDataItemAction = "DeleteDataItem";
    internal const string DownloadDataItemAction = "DataItem.Download";

    internal const string DataItemTargetType = "DataItem";
    internal const string NotFoundErrorCode = "DataItem.NotFound";

    // The fields of AuditRecordSummary, as serialized (camelCase); nothing else may be returned
    internal static readonly string[] SummaryFields =
        ["id", "occurredAt", "actorUserId", "actorEmail", "action", "targetType", "targetId", "outcome", "errorCode"];

    // A time far after the setup's own records (written now), so seeded records are the newest
    internal static readonly DateTime FutureTime = new(2030, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    // Guid.ToByteArray keeps the last group's bytes in order, so this byte is the id's last
    private const int LastIdByte = 15;

    internal static string OrganizationAuditRecordsEndpoint(Guid organizationId) => $"/organizations/{organizationId}/audit-records";

    internal static DbAuditRecord Record(
        string action, DateTime occurredAt, Guid? actorUserId = null, Guid? organizationId = null,
        AuditOutcome outcome = AuditOutcome.Succeeded, string? errorCode = null, Guid? targetId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            OccurredAt = occurredAt,
            ActorUserId = actorUserId,
            Action = action,
            TargetType = targetId is null ? null : DataItemTargetType,
            TargetId = targetId,
            OrganizationId = organizationId,
            Outcome = outcome.ToString(),
            ErrorCode = errorCode
        };

    // Records with ids in ascending order. The ids share all but their last byte, so .NET, SQLite (text) and
    // SQL Server (uniqueidentifier) agree on that order
    internal static DbAuditRecord[] TiedRecords(int count, Func<int, DbAuditRecord> create)
    {
        var prefix = Guid.NewGuid().ToByteArray();
        return Enumerable.Range(0, count).Select(i =>
        {
            var record = create(i);
            prefix[LastIdByte] = (byte)i;
            record.Id = new Guid(prefix);
            return record;
        }).ToArray();
    }

    internal static async Task SeedAsync(ApiFactory factory, params DbAuditRecord[] records)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        db.AuditRecords.AddRange(records);
        await db.SaveChangesAsync();
    }

    internal static async Task<List<DbAuditRecord>> StoredRecordsAsync(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().AuditRecords.AsNoTracking().ToListAsync();
    }

    internal static async Task<List<AuditRecordSummaryResponse>> ReadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<AuditRecordSummaryResponse>>())!;
    }

    internal static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>())!;
    }

    // The optional filters as a query string; times in ISO 8601 ("O"), so a UTC time ends with Z
    internal static string Filters(Guid? userId = null, string? action = null, DateTime? from = null, DateTime? to = null)
    {
        var parts = new List<string>();
        if (userId is not null) parts.Add($"userId={userId}");
        if (action is not null) parts.Add($"action={Uri.EscapeDataString(action)}");
        if (from is not null) parts.Add($"from={Uri.EscapeDataString(from.Value.ToString("O"))}");
        if (to is not null) parts.Add($"to={Uri.EscapeDataString(to.Value.ToString("O"))}");
        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }
}

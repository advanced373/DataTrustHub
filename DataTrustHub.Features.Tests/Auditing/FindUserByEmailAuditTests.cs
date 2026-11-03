using System.Net;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.FindUserByEmail;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Auditing.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Auditing;

// #122 Part B through the API (ADR-0025): every find-by-email call that reaches the handler pipeline appends
// exactly one audit record (AC4), other queries append none (AC5), and neither the record nor any log line
// holds the email searched for (AC7). Tests in a class run one after another, so "the records added by this
// request" is exact; each test has its own callers, so the per-caller rate limit never interferes
public class FindUserByEmailAuditTests(CapturingLogsApiFactory factory) : IClassFixture<CapturingLogsApiFactory>
{
    private const string EmailDomain = "@example.com";
    private const string MalformedEmail = "not-an-email-lookup_malformed";
    private const string FindUserByEmailAction = "FindUserByEmail";
    private const string SucceededOutcome = "Succeeded";
    private const string RejectedOutcome = "Rejected";

    // --- AC4: one record per lookup ---

    [Fact]
    public async Task FindUserByEmail_Found_RecordsSucceededWithTheUserAsTarget()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "lookup_audit_found");
        var (_, userId) = await CreateUserAsync(factory, "lookup_audit_found_target");
        var email = $"lookup_audit_found_target{EmailDomain}";

        var (response, records) = await LookupAsync(scenario.OrgAdmin, scenario.OrganizationId, email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var record = Assert.Single(records);
        AssertRecord(record, scenario.OrgAdminId, scenario.OrganizationId, SucceededOutcome, null);
        Assert.Equal(((string?)AuditTargetTypes.User, (Guid?)userId), (record.TargetType, record.TargetId));
        AssertHoldsNo(record, email);
    }

    [Fact]
    public async Task FindUserByEmail_UserNotFound_RecordsRejectedWithTheCodeAndNoTarget()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "lookup_audit_unknown");
        var email = $"lookup_audit_unknown_nobody{EmailDomain}";

        var (response, records) = await LookupAsync(scenario.OrgAdmin, scenario.OrganizationId, email);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var record = Assert.Single(records);
        AssertRecord(record, scenario.OrgAdminId, scenario.OrganizationId, RejectedOutcome, AdminErrorCodes.UserNotFound);
        AssertNoTarget(record);
        AssertHoldsNo(record, email);
    }

    // The caller is not an organization admin here: the lookup is refused before any user is found
    [Fact]
    public async Task FindUserByEmail_CallerNotOrgAdmin_RecordsRejectedWithTheRouteOrganizationAndNoTarget()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "lookup_audit_outsider");
        var (outsider, outsiderId) = await CreateUserAsync(factory, "lookup_audit_outsider_caller");
        await CreateUserAsync(factory, "lookup_audit_outsider_target");
        var email = $"lookup_audit_outsider_target{EmailDomain}";

        var (response, records) = await LookupAsync(outsider, scenario.OrganizationId, email);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var record = Assert.Single(records);
        AssertRecord(record, outsiderId, scenario.OrganizationId, RejectedOutcome, AdminErrorCodes.OrganizationNotFound);
        AssertNoTarget(record);
        AssertHoldsNo(record, email);
    }

    [Fact]
    public async Task FindUserByEmail_UnknownOrganization_RecordsRejectedWithTheRouteOrganization()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "lookup_audit_noorg");
        var unknownOrganizationId = Guid.NewGuid();

        var (_, records) = await LookupAsync(scenario.OrgAdmin, unknownOrganizationId, $"lookup_audit_noorg_orgadmin{EmailDomain}");

        var record = Assert.Single(records);
        AssertRecord(record, scenario.OrgAdminId, unknownOrganizationId, RejectedOutcome, AdminErrorCodes.OrganizationNotFound);
        AssertNoTarget(record);
    }

    // The audit decorator wraps the validation decorator, so a lookup the validator refuses is recorded too
    [Fact]
    public async Task FindUserByEmail_InvalidEmail_RecordsTheValidationRejection()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "lookup_audit_invalid");

        var (response, records) = await LookupAsync(scenario.OrgAdmin, scenario.OrganizationId, MalformedEmail);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var record = Assert.Single(records);
        AssertRecord(record, scenario.OrgAdminId, scenario.OrganizationId, RejectedOutcome, AdminErrorCodes.ValidationGeneral);
        AssertNoTarget(record);
        AssertHoldsNo(record, MalformedEmail);
    }

    [Fact]
    public async Task FindUserByEmail_MissingEmail_RecordsTheValidationRejection()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "lookup_audit_missing");

        var (response, records) = await LookupAsync(scenario.OrgAdmin, scenario.OrganizationId, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var record = Assert.Single(records);
        AssertRecord(record, scenario.OrgAdminId, scenario.OrganizationId, RejectedOutcome, AdminErrorCodes.ValidationGeneral);
    }

    // An anonymous request is refused by authorization before the handler pipeline, so it leaves no record
    [Fact]
    public async Task FindUserByEmail_Anonymous_RecordsNothing()
    {
        var (response, records) = await LookupAsync(factory.CreateClient(), Guid.NewGuid(), $"lookup_audit_anonymous{EmailDomain}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(records);
    }

    [Fact]
    public async Task FindUserByEmail_TwoLookups_RecordOnePerLookup()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "lookup_audit_twice");

        var records = await RecordsAddedByAsync(async () =>
        {
            await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, $"lookup_audit_twice_orgadmin{EmailDomain}");
            return await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, $"lookup_audit_twice_nobody{EmailDomain}");
        });

        Assert.Equal([RejectedOutcome, SucceededOutcome], records.Select(r => r.Outcome).Order());
    }

    [Fact]
    public void FindUserByEmailHandler_IsWrappedOutermostByTheQueryAuditDecorator()
    {
        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<FindUserByEmailQuery, Result<FindUserByEmailResponse>>>();

        Assert.Equal(AuditDecoratorHarness.QueryDecoratorDefinition, handler.GetType().GetGenericTypeDefinition());
    }

    // --- AC5: queries without IAuditedQuery ---

    [Fact]
    public async Task OtherQueries_LeaveNoAuditRecord()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "lookup_audit_other_queries");

        var records = await RecordsAddedByAsync(async () =>
        {
            await scenario.OrgAdmin.GetAsync(MembersEndpoint(scenario.OrganizationId));
            return await scenario.OrgAdmin.GetAsync(MyOrganizationsEndpoint);
        });

        Assert.Empty(records);
    }

    // --- AC7: no log line holds the email searched for ---

    [Fact]
    public async Task FindUserByEmail_EveryOutcome_LogsNoSearchedEmail()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "lookup_audit_logs");
        var (outsider, _) = await CreateUserAsync(factory, "lookup_audit_logs_outsider");
        await CreateUserAsync(factory, "lookup_audit_logs_target");
        var found = $"lookup_audit_logs_target{EmailDomain}";
        var unknown = $"lookup_audit_logs_nobody{EmailDomain}";
        var linesBefore = factory.LogLines.Count;

        await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, found);
        await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, unknown);
        await PostFindUserByEmailAsync(outsider, scenario.OrganizationId, found);
        await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, MalformedEmail);

        var lines = factory.LogLines.Skip(linesBefore).ToList();
        string[] searched = [found, unknown, MalformedEmail];
        Assert.All(searched, email => Assert.DoesNotContain(lines, line => line.Contains(email, StringComparison.OrdinalIgnoreCase)));
    }

    // --- helpers ---

    private async Task<(HttpResponseMessage Response, List<DbAuditRecord> Records)> LookupAsync(
        HttpClient caller, Guid organizationId, string? email)
    {
        HttpResponseMessage? response = null;
        var records = await RecordsAddedByAsync(async () =>
            response = await PostFindUserByEmailAsync(caller, organizationId, email));
        return (response!, records);
    }

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

    private static void AssertRecord(DbAuditRecord record, Guid actorUserId, Guid organizationId, string outcome, string? errorCode) =>
        Assert.Equal(
            (FindUserByEmailAction, (Guid?)actorUserId, (Guid?)organizationId, outcome, errorCode),
            (record.Action, record.ActorUserId, record.OrganizationId, record.Outcome, record.ErrorCode));

    private static void AssertNoTarget(DbAuditRecord record) =>
        Assert.Equal(((string?)null, (Guid?)null), (record.TargetType, record.TargetId));

    // No text column of the record holds the email searched for
    private static void AssertHoldsNo(DbAuditRecord record, string email)
    {
        string?[] texts = [record.Action, record.TargetType, record.Outcome, record.ErrorCode];
        Assert.All(texts, text => Assert.DoesNotContain(email, text ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }
}

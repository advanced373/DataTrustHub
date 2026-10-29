using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Authentication.RegisterUser;
using DataTrustHub.Features.DataManagement.ListDataItems;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Authentication.Helpers;
using DataTrustHub.Features.Tests.Auditing.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Auditing;

// AC5, AC6 and AC7 of #24 step 1 through the API: real commands leave one audit record each, with the
// actor from the JWT, the organization and the target; register and log in are anonymous and hold no email;
// queries leave none. Tests in a class run one after another, so "the records added by this request" is exact
public class CommandAuditTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string RegisterEndpoint = "/auth/register";
    private const string LoginEndpoint = "/auth/login";
    private const string UploadEndpoint = "/data/upload";
    private const string DataItemsEndpoint = "/data";
    private const string ClassificationOptionsEndpoint = "/data/classification-options";
    private const string Password = "Password1!";
    private const string WrongPassword = "WrongPassword1!";
    private const string InvalidEmail = "not-an-email";
    private const string ShortPassword = "short";
    private const string UnknownRole = "Overlord";
    private const int Confidential = 2;
    private const int Secret = 3;

    private const string RegisterUserAction = "RegisterUser";
    private const string LoginUserAction = "LoginUser";
    private const string AddDataItemAction = "AddDataItem";
    private const string DeleteDataItemAction = "DeleteDataItem";
    private const string AddMemberAction = "AddMember";
    private const string SucceededOutcome = "Succeeded";
    private const string RejectedOutcome = "Rejected";
    private const string ValidationCode = "Validation.General";
    private const string DataItemNotFoundCode = "DataItem.NotFound";
    private const string DataItemNotClearedCode = "DataItem.NotCleared";

    // --- AC7: register and log in are anonymous and hold no email ---

    [Fact]
    public async Task Register_Succeeded_RecordsAnonymousWithoutTargetOrganizationOrEmail()
    {
        var email = NewEmail("audit_register");

        var records = await RecordsAddedByAsync(() => factory.CreateClient().PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password }));

        var record = Assert.Single(records);
        AssertRecord(record, RegisterUserAction, null, SucceededOutcome, null);
        AssertNoTargetOrOrganization(record);
        AssertHoldsNo(record, email);
    }

    [Fact]
    public async Task Register_EmailInUse_RecordsRejectedWithTheErrorCodeAndNoEmail()
    {
        var email = NewEmail("audit_register_taken");
        await factory.CreateClient().PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password });

        var records = await RecordsAddedByAsync(() => factory.CreateClient().PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password }));

        var record = Assert.Single(records);
        AssertRecord(record, RegisterUserAction, null, RejectedOutcome, AuthErrorCodes.EmailAlreadyInUse);
        AssertHoldsNo(record, email);
    }

    // The audit decorator wraps the validation decorator, so a request the validator refuses is recorded too
    [Fact]
    public async Task Register_InvalidInput_RecordsTheValidationRejection()
    {
        var records = await RecordsAddedByAsync(() =>
            factory.CreateClient().PostAsJsonAsync(RegisterEndpoint, new { Email = InvalidEmail, Password = ShortPassword }));

        var record = Assert.Single(records);
        AssertRecord(record, RegisterUserAction, null, RejectedOutcome, ValidationCode);
        AssertHoldsNo(record, InvalidEmail);
    }

    [Fact]
    public async Task Register_InvalidInput_StillAnswers400()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(RegisterEndpoint, new { Email = InvalidEmail, Password = ShortPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_Succeeded_RecordsAnonymousWithoutEmail()
    {
        var email = NewEmail("audit_login");
        await factory.CreateClient().PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password });

        var records = await RecordsAddedByAsync(() => factory.CreateClient().PostAsJsonAsync(LoginEndpoint, new { Email = email, Password }));

        var record = Assert.Single(records);
        AssertRecord(record, LoginUserAction, null, SucceededOutcome, null);
        AssertNoTargetOrOrganization(record);
        AssertHoldsNo(record, email);
    }

    [Fact]
    public async Task Login_WrongPassword_RecordsRejectedAnonymousWithoutEmail()
    {
        var email = NewEmail("audit_login_wrong");
        await factory.CreateClient().PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password });

        var records = await RecordsAddedByAsync(() =>
            factory.CreateClient().PostAsJsonAsync(LoginEndpoint, new { Email = email, Password = WrongPassword }));

        var record = Assert.Single(records);
        AssertRecord(record, LoginUserAction, null, RejectedOutcome, AuthErrorCodes.InvalidCredentials);
        AssertHoldsNo(record, email);
    }

    // --- AC6: the data item commands ---

    [Fact]
    public async Task DeleteDataItem_Classified_RecordsTheActorTargetAndPolicyOrganization()
    {
        var cleared = await CreateClearedUserAsync("audit_delete_classified", Secret);
        var itemId = await UploadAndReadIdAsync(cleared.Client, cleared.PolicyId, Confidential);

        var records = await RecordsAddedByAsync(() => cleared.Client.DeleteAsync($"{DataItemsEndpoint}/{itemId}"));

        var record = Assert.Single(records);
        AssertRecord(record, DeleteDataItemAction, cleared.UserId, SucceededOutcome, null);
        AssertTarget(record, AuditTargetTypes.DataItem, itemId);
        Assert.Equal(cleared.OrganizationId, record.OrganizationId);
    }

    [Fact]
    public async Task DeleteDataItem_Personal_RecordsTheTargetWithoutOrganization()
    {
        var (client, userId) = await CreateUserAsync(factory, "audit_delete_personal");
        var itemId = await UploadAndReadIdAsync(client, null, null);

        var records = await RecordsAddedByAsync(() => client.DeleteAsync($"{DataItemsEndpoint}/{itemId}"));

        var record = Assert.Single(records);
        AssertRecord(record, DeleteDataItemAction, userId, SucceededOutcome, null);
        AssertTarget(record, AuditTargetTypes.DataItem, itemId);
        Assert.Null(record.OrganizationId);
    }

    [Fact]
    public async Task DeleteDataItem_MissingItem_RecordsRejectedWithTheTarget()
    {
        var (client, userId) = await CreateUserAsync(factory, "audit_delete_missing");
        var missingId = Guid.NewGuid();

        var records = await RecordsAddedByAsync(() => client.DeleteAsync($"{DataItemsEndpoint}/{missingId}"));

        var record = Assert.Single(records);
        AssertRecord(record, DeleteDataItemAction, userId, RejectedOutcome, DataItemNotFoundCode);
        AssertTarget(record, AuditTargetTypes.DataItem, missingId);
    }

    [Fact]
    public async Task AddDataItem_Classified_RecordsTheNewItemAndPolicyOrganization()
    {
        var cleared = await CreateClearedUserAsync("audit_add_classified", Secret);
        Guid itemId = default;

        var records = await RecordsAddedByAsync(async () =>
        {
            var response = await UploadAsync(cleared.Client, cleared.PolicyId, Confidential);
            itemId = (await response.Content.ReadFromJsonAsync<AddDataItemResponse>())!.DataItemId;
            return response;
        });

        var record = Assert.Single(records);
        AssertRecord(record, AddDataItemAction, cleared.UserId, SucceededOutcome, null);
        AssertTarget(record, AuditTargetTypes.DataItem, itemId);
        Assert.Equal(cleared.OrganizationId, record.OrganizationId);
    }

    [Fact]
    public async Task AddDataItem_Personal_RecordsTheNewItemWithoutOrganization()
    {
        var (client, userId) = await CreateUserAsync(factory, "audit_add_personal");
        Guid itemId = default;

        var records = await RecordsAddedByAsync(async () =>
        {
            var response = await UploadAsync(client, null, null);
            itemId = (await response.Content.ReadFromJsonAsync<AddDataItemResponse>())!.DataItemId;
            return response;
        });

        var record = Assert.Single(records);
        AssertRecord(record, AddDataItemAction, userId, SucceededOutcome, null);
        AssertTarget(record, AuditTargetTypes.DataItem, itemId);
        Assert.Null(record.OrganizationId);
    }

    [Fact]
    public async Task AddDataItem_NotCleared_RecordsRejectedWithoutTarget()
    {
        var cleared = await CreateClearedUserAsync("audit_add_rejected", Confidential);

        var records = await RecordsAddedByAsync(() => UploadAsync(cleared.Client, cleared.PolicyId, Secret));

        var record = Assert.Single(records);
        AssertRecord(record, AddDataItemAction, cleared.UserId, RejectedOutcome, DataItemNotClearedCode);
        Assert.Equal(((string?)null, (Guid?)null), (record.TargetType, record.TargetId));
    }

    // --- AC6: a membership command ---

    [Fact]
    public async Task AddMember_RecordsTheOrgAdminTheOrganizationAndTheUser()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_add_member");
        var (_, userId) = await CreateUserAsync(factory, "audit_add_member_user");

        var records = await RecordsAddedByAsync(() => AdminSetup.PostMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, MemberRole));

        var record = Assert.Single(records);
        AssertRecord(record, AddMemberAction, scenario.OrgAdminId, SucceededOutcome, null);
        AssertTarget(record, AuditTargetTypes.User, userId);
        Assert.Equal(scenario.OrganizationId, record.OrganizationId);
    }

    [Fact]
    public async Task AddMember_ByOutsider_RecordsRejectedWithTheOrganizationAndTheUser()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_add_member_outsider");
        var (outsider, outsiderId) = await CreateUserAsync(factory, "audit_add_member_outsider_caller");
        var (_, userId) = await CreateUserAsync(factory, "audit_add_member_outsider_user");

        var records = await RecordsAddedByAsync(() => AdminSetup.PostMemberAsync(outsider, scenario.OrganizationId, userId, MemberRole));

        var record = Assert.Single(records);
        AssertRecord(record, AddMemberAction, outsiderId, RejectedOutcome, AdminErrorCodes.OrganizationNotFound);
        AssertTarget(record, AuditTargetTypes.User, userId);
        Assert.Equal(scenario.OrganizationId, record.OrganizationId);
    }

    [Fact]
    public async Task AddMember_InvalidRole_RecordsTheValidationRejectionWithTheActor()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_add_member_invalid");
        var (_, userId) = await CreateUserAsync(factory, "audit_add_member_invalid_user");

        var records = await RecordsAddedByAsync(() => AdminSetup.PostMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, UnknownRole));

        var record = Assert.Single(records);
        AssertRecord(record, AddMemberAction, scenario.OrgAdminId, RejectedOutcome, ValidationCode);
    }

    // --- AC7: queries are not audited (the download GET /data/{id} is, see DataAccessAuditTests, #24 step 2) ---

    [Fact]
    public async Task Queries_LeaveNoAuditRecord()
    {
        var cleared = await CreateClearedUserAsync("audit_queries", Secret);
        await UploadAndReadIdAsync(cleared.Client, cleared.PolicyId, Confidential);

        var records = await RecordsAddedByAsync(async () =>
        {
            await cleared.Client.GetAsync(DataItemsEndpoint);
            await cleared.Client.GetAsync(ClassificationOptionsEndpoint);
            return await cleared.Client.GetAsync(AdminSetup.MyOrganizationsEndpoint);
        });

        Assert.Empty(records);
    }

    // --- AC5: the wiring ---

    [Fact]
    public void CommandHandler_IsWrappedOutermostByTheAuditDecorator()
    {
        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RegisterUserCommand, Result<Guid>>>();

        Assert.Equal(AuditDecoratorHarness.DecoratorDefinition, handler.GetType().GetGenericTypeDefinition());
    }

    [Fact]
    public void QueryHandler_IsNotWrappedByTheAuditDecorator()
    {
        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<ListDataItemsQuery, Result<IReadOnlyList<DataItemSummary>>>>();

        Assert.False(handler.GetType().IsGenericType
            && handler.GetType().GetGenericTypeDefinition() == AuditDecoratorHarness.DecoratorDefinition);
    }

    [Fact]
    public void AuditScope_IsScopedPerRequest()
    {
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();

        Assert.Same(first.ServiceProvider.GetRequiredService<AuditScope>(), first.ServiceProvider.GetRequiredService<AuditScope>());
        Assert.NotSame(first.ServiceProvider.GetRequiredService<AuditScope>(), second.ServiceProvider.GetRequiredService<AuditScope>());
    }

    // --- helpers ---

    private static string NewEmail(string prefix) => $"{prefix}_{Guid.NewGuid():N}@example.com";

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

    private static void AssertRecord(DbAuditRecord record, string action, Guid? actorUserId, string outcome, string? errorCode) =>
        Assert.Equal((action, actorUserId, outcome, errorCode), (record.Action, record.ActorUserId, record.Outcome, record.ErrorCode));

    private static void AssertTarget(DbAuditRecord record, string targetType, Guid targetId) =>
        Assert.Equal(((string?)targetType, (Guid?)targetId), (record.TargetType, record.TargetId));

    private static void AssertNoTargetOrOrganization(DbAuditRecord record) =>
        Assert.Equal(((string?)null, (Guid?)null, (Guid?)null), (record.TargetType, record.TargetId, record.OrganizationId));

    // No text column of the record holds the value (ADR-0016 point 3)
    private static void AssertHoldsNo(DbAuditRecord record, string value)
    {
        string?[] texts = [record.Action, record.TargetType, record.Outcome, record.ErrorCode];
        Assert.All(texts, text => Assert.DoesNotContain(value, text ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    // A user cleared on a policy of a new organization, as a member there (C5, C6)
    private async Task<ClearedUser> CreateClearedUserAsync(string prefix, int priority)
    {
        var admin = await TestUsers.CreateClientAsync(factory, $"{prefix}_admin@example.com", UserRoles.Admin);
        var (client, userId) = await CreateUserAsync(factory, $"{prefix}_user");
        var (organizationId, orgAdmin, policyId) = await AdminSetup.CreatePolicyWithOrgAdminAsync(factory, admin, $"{prefix} policy");
        await AdminSetup.AddMemberAndGrantClearanceAsync(orgAdmin, organizationId, userId, policyId, $"Level {priority}", priority);
        return new ClearedUser(client, userId, organizationId, policyId);
    }

    private static async Task<Guid> UploadAndReadIdAsync(HttpClient client, Guid? policyId, int? levelPriority)
    {
        var response = await UploadAsync(client, policyId, levelPriority);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AddDataItemResponse>())!.DataItemId;
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid? policyId, int? levelPriority)
    {
        var file = new ByteArrayContent("audited"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var form = new MultipartFormDataContent { { file, "file", "audited.txt" } };
        if (policyId is not null) form.Add(new StringContent(policyId.ToString()!), "policyId");
        if (levelPriority is not null) form.Add(new StringContent(levelPriority.ToString()!), "levelPriority");
        return client.PostAsync(UploadEndpoint, form);
    }

    private sealed record ClearedUser(HttpClient Client, Guid UserId, Guid OrganizationId, Guid PolicyId);

    private sealed record AddDataItemResponse(Guid DataItemId);
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Registrations;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Authentication.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AuditTrailSetup;

namespace DataTrustHub.Features.Tests.Administration;

// #172 API 2 through the API, on SQLite (the list purges with a set-based delete): the platform admin lists,
// approves and rejects registration requests. The host's clock is frozen; an expired request is stored with a
// time older than the pending lifetime before that clock, through the domain and the repository. Tests share
// the host, so each one reads only its own requests, users and audit records
public class RegistrationRequestAdminTests(RegistrationAdminApiFactory factory) : IClassFixture<RegistrationAdminApiFactory>
{
    private const string RequestsEndpoint = "/admin/registration-requests";
    private const string RegisterEndpoint = "/auth/register";
    private const string LoginEndpoint = "/auth/login";
    private const string EmailDomain = "@example.com";
    private const string Password = "Original1!";
    private const string WrongPassword = "Different1!";
    private const string SucceededOutcome = "Succeeded";
    private const string RejectedOutcome = "Rejected";
    private const string ApproveRoute = "approve";
    private const string RejectRoute = "reject";
    private const string GetMethod = "GET";
    private const string PostMethod = "POST";

    // The fields of a listed request, as serialized (camelCase); nothing else may be returned
    private static readonly string[] RequestSummaryFields = ["id", "email", "requestedAt", "expiresAt"];

    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Lifetime = RegistrationAdminApiFactory.Lifetime;

    public static TheoryData<string, string> Routes() => new()
    {
        { GetMethod, RequestsEndpoint },
        { PostMethod, ApproveEndpoint(Guid.NewGuid()) },
        { PostMethod, RejectEndpoint(Guid.NewGuid()) }
    };

    // --- AC1: platform admin only ---

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Route_Anonymous_Returns401(string method, string route)
    {
        var response = await SendAsync(factory.CreateClient(), method, route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Route_SignedInUser_Returns403(string method, string route)
    {
        var user = await TestUsers.CreateClientAsync(factory, Email($"regadmin_user_{Guid.NewGuid():N}"));

        var response = await SendAsync(user, method, route);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Route_OrganizationAdmin_Returns403(string method, string route)
    {
        var scenario = await MembershipSetup.CreateOrganizationWithOrgAdminAsync(factory, $"regadmin_orgadmin_{Guid.NewGuid():N}");

        var response = await SendAsync(scenario.OrgAdmin, method, route);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ApproveAndReject_OrganizationAdmin_ChangeNothing()
    {
        var scenario = await MembershipSetup.CreateOrganizationWithOrgAdminAsync(factory, "regadmin_orgadmin_nochange");
        var email = Email("regadmin_orgadmin_nochange_requester");
        var requestId = await RegisterAsync(email);

        await scenario.OrgAdmin.PostAsync(ApproveEndpoint(requestId), null);
        await scenario.OrgAdmin.PostAsync(RejectEndpoint(requestId), null);

        Assert.True(await RequestStoredAsync(requestId));
        Assert.Empty(await StoredUsersAsync(email));
    }

    // --- AC2: list ---

    [Fact]
    public async Task List_PendingRequests_ReturnsThemOldestFirst()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_list_order");
        var now = factory.Clock.UtcNow;
        var newest = await SeedRequestAsync(Email("regadmin_list_order_c"), now - TimeSpan.FromHours(1));
        var oldest = await SeedRequestAsync(Email("regadmin_list_order_a"), now - TimeSpan.FromHours(3));
        var middle = await SeedRequestAsync(Email("regadmin_list_order_b"), now - TimeSpan.FromHours(2));
        HashSet<Guid> mine = [oldest.Id, middle.Id, newest.Id];

        var listed = await ListAsync(admin);

        Assert.Equal([oldest.Id, middle.Id, newest.Id], listed.Where(r => mine.Contains(r.Id)).Select(r => r.Id));
    }

    // The email as stored (normalized, U7); the times in UTC; ExpiresAt is RequestedAt plus the pending lifetime
    [Fact]
    public async Task List_PendingRequest_ReturnsItsIdNormalizedEmailRequestedAtAndExpiresAt()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_list_fields");
        var requestId = await RegisterAsync(" RegAdmin_List_Fields_Requester@Example.COM ");
        var requestedAt = factory.Clock.UtcNow;

        var summary = Assert.Single(await ListAsync(admin), r => r.Id == requestId);

        Assert.Equal(
            (Email("regadmin_list_fields_requester"), requestedAt, requestedAt + Lifetime),
            (summary.Email, summary.RequestedAt.ToUniversalTime(), summary.ExpiresAt.ToUniversalTime()));
    }

    [Fact]
    public async Task List_PendingRequest_ReturnsOnlyIdEmailRequestedAtAndExpiresAt()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_list_shape");
        var requestId = await RegisterAsync(Email("regadmin_list_shape_requester"));

        var response = await admin.GetAsync(RequestsEndpoint);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = json.EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == requestId);
        Assert.Equal(RequestSummaryFields.Order(), item.EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task List_PendingRequest_NeverReturnsThePasswordHash()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_list_hash");
        var requestId = await RegisterAsync(Email("regadmin_list_hash_requester"));
        var hash = await StoredHashAsync(requestId);

        var body = await (await admin.GetAsync(RequestsEndpoint)).Content.ReadAsStringAsync();

        Assert.DoesNotContain(hash, body);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_ExpiredRequest_IsNotReturned()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_list_expired");
        var expired = await SeedRequestAsync(Email("regadmin_list_expired_requester"), factory.Clock.UtcNow - Lifetime - OneSecond);

        var listed = await ListAsync(admin);

        Assert.DoesNotContain(listed, r => r.Id == expired.Id);
    }

    [Fact]
    public async Task List_ExpiredRequest_IsPurgedByTheCall()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_list_purge");
        var expired = await SeedRequestAsync(Email("regadmin_list_purge_requester"), factory.Clock.UtcNow - Lifetime - OneSecond);
        Assert.True(await RequestStoredAsync(expired.Id));

        (await admin.GetAsync(RequestsEndpoint)).EnsureSuccessStatusCode();

        Assert.False(await RequestStoredAsync(expired.Id));
    }

    // Exactly the lifetime old is not older than the lifetime: still pending, listed and kept
    [Fact]
    public async Task List_RequestExactlyTheLifetimeOld_IsReturnedAndKept()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_list_boundary");
        var boundary = await SeedRequestAsync(Email("regadmin_list_boundary_requester"), factory.Clock.UtcNow - Lifetime);

        var listed = await ListAsync(admin);

        Assert.Contains(listed, r => r.Id == boundary.Id);
        Assert.True(await RequestStoredAsync(boundary.Id));
    }

    // --- AC3: approve a pending request ---

    [Fact]
    public async Task Approve_PendingRequest_Returns200WithTheNewUserId()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_approve_ok");
        var email = Email("regadmin_approve_ok_requester");
        var requestId = await RegisterAsync(email);

        var response = await admin.PostAsync(ApproveEndpoint(requestId), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var userId = (await response.Content.ReadFromJsonAsync<ApproveResponse>())!.UserId;
        Assert.Equal(userId, Assert.Single(await StoredUsersAsync(email)).Id);
    }

    [Fact]
    public async Task Approve_PendingRequest_CreatesOneUserWithTheNormalizedEmailRoleUserAndTheRequestsHash()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_approve_user");
        var email = Email("regadmin_approve_user_requester");
        var requestId = await RegisterAsync(" RegAdmin_Approve_User_Requester@EXAMPLE.com ");
        var hash = await StoredHashAsync(requestId);

        (await admin.PostAsync(ApproveEndpoint(requestId), null)).EnsureSuccessStatusCode();

        var user = Assert.Single(await StoredUsersAsync(email));
        Assert.Equal((email, UserRoles.User, hash), (user.Email, user.Role, user.HashedPassword));
    }

    [Fact]
    public async Task Approve_PendingRequest_RemovesTheRequest()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_approve_gone");
        var requestId = await RegisterAsync(Email("regadmin_approve_gone_requester"));

        (await admin.PostAsync(ApproveEndpoint(requestId), null)).EnsureSuccessStatusCode();

        Assert.False(await RequestStoredAsync(requestId));
        Assert.DoesNotContain(await ListAsync(admin), r => r.Id == requestId);
    }

    [Fact]
    public async Task Approve_PendingRequest_TheRequesterLogsInWithTheOriginalPassword()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_approve_login");
        var email = Email("regadmin_approve_login_requester");
        var requestId = await RegisterAsync(email);
        (await admin.PostAsync(ApproveEndpoint(requestId), null)).EnsureSuccessStatusCode();

        var login = await LoginAsync(email, Password);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.False(string.IsNullOrEmpty((await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token));
    }

    [Fact]
    public async Task Approve_PendingRequest_TheRequesterCannotLogInWithAnotherPassword()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_approve_wrong");
        var email = Email("regadmin_approve_wrong_requester");
        (await admin.PostAsync(ApproveEndpoint(await RegisterAsync(email)), null)).EnsureSuccessStatusCode();

        var login = await LoginAsync(email, WrongPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Approve_RequestExactlyTheLifetimeOld_Returns200()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_approve_boundary");
        var boundary = await SeedRequestAsync(Email("regadmin_approve_boundary_requester"), factory.Clock.UtcNow - Lifetime);

        var response = await admin.PostAsync(ApproveEndpoint(boundary.Id), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- AC4: approve refused ---

    [Fact]
    public async Task Approve_UnknownId_Returns404NotFound()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_approve_unknown");

        var response = await admin.PostAsync(ApproveEndpoint(Guid.NewGuid()), null);

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.NotFound,
            AdminErrorCodes.RegistrationRequestNotFound, AdminErrorMessages.RegistrationRequestNotFound);
    }

    [Fact]
    public async Task Approve_ExpiredRequest_Returns404NotFound()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_approve_expired");
        var expired = await SeedRequestAsync(Email("regadmin_approve_expired_requester"), factory.Clock.UtcNow - Lifetime - OneSecond);

        var response = await admin.PostAsync(ApproveEndpoint(expired.Id), null);

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.NotFound,
            AdminErrorCodes.RegistrationRequestNotFound, AdminErrorMessages.RegistrationRequestNotFound);
    }

    // Nothing changes: no user, and the expired request is left for the purge
    [Fact]
    public async Task Approve_ExpiredRequest_CreatesNoUserAndLeavesTheRequest()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_approve_expired_nochange");
        var email = Email("regadmin_approve_expired_nochange_requester");
        var expired = await SeedRequestAsync(email, factory.Clock.UtcNow - Lifetime - OneSecond);

        await admin.PostAsync(ApproveEndpoint(expired.Id), null);

        Assert.Empty(await StoredUsersAsync(email));
        Assert.True(await RequestStoredAsync(expired.Id));
    }

    [Fact]
    public async Task Approve_UnknownId_AddsNoUser()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_approve_unknown_nochange");
        var usersBefore = await UserCountAsync();

        await admin.PostAsync(ApproveEndpoint(Guid.NewGuid()), null);

        Assert.Equal(usersBefore, await UserCountAsync());
    }

    [Fact]
    public async Task Approve_EmailAlreadyHasAnAccount_Returns409EmailAlreadyInUse()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_approve_taken");
        var email = Email("regadmin_approve_taken_requester");
        var requestId = await RegisterAsync(email);
        await TestUsers.CreateAsync(factory, email);

        var response = await admin.PostAsync(ApproveEndpoint(requestId), null);

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.Conflict,
            AuthErrorCodes.EmailAlreadyInUse, AuthErrorMessages.EmailAlreadyInUse);
    }

    [Fact]
    public async Task Approve_EmailAlreadyHasAnAccount_AddsNoUserAndKeepsTheRequest()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_approve_taken_nochange");
        var email = Email("regadmin_approve_taken_nochange_requester");
        var requestId = await RegisterAsync(email);
        var existingId = await TestUsers.CreateAsync(factory, email);

        await admin.PostAsync(ApproveEndpoint(requestId), null);

        Assert.Equal(existingId, Assert.Single(await StoredUsersAsync(email)).Id);
        Assert.True(await RequestStoredAsync(requestId));
    }

    [Fact]
    public async Task Approve_AlreadyApprovedRequest_Returns404AndAddsNoSecondUser()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_approve_twice");
        var email = Email("regadmin_approve_twice_requester");
        var requestId = await RegisterAsync(email);
        (await admin.PostAsync(ApproveEndpoint(requestId), null)).EnsureSuccessStatusCode();

        var again = await admin.PostAsync(ApproveEndpoint(requestId), null);

        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Single(await StoredUsersAsync(email));
    }

    // --- AC5: reject ---

    [Fact]
    public async Task Reject_PendingRequest_Returns204AndRemovesTheRequest()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_reject_ok");
        var requestId = await RegisterAsync(Email("regadmin_reject_ok_requester"));

        var response = await admin.PostAsync(RejectEndpoint(requestId), null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await RequestStoredAsync(requestId));
    }

    [Fact]
    public async Task Reject_PendingRequest_CreatesNoUser()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_reject_nouser");
        var email = Email("regadmin_reject_nouser_requester");
        var requestId = await RegisterAsync(email);

        await admin.PostAsync(RejectEndpoint(requestId), null);

        Assert.Empty(await StoredUsersAsync(email));
    }

    [Fact]
    public async Task Reject_PendingRequest_TheSameEmailCanRegisterAgain()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_reject_again");
        var email = Email("regadmin_reject_again_requester");
        var requestId = await RegisterAsync(email);
        (await admin.PostAsync(RejectEndpoint(requestId), null)).EnsureSuccessStatusCode();

        var response = await PostRegisterAsync(email);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Reject_UnknownId_Returns404NotFound()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_reject_unknown");

        var response = await admin.PostAsync(RejectEndpoint(Guid.NewGuid()), null);

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.NotFound,
            AdminErrorCodes.RegistrationRequestNotFound, AdminErrorMessages.RegistrationRequestNotFound);
    }

    // The spec: an expired request can be rejected; it is deleted either way
    [Fact]
    public async Task Reject_ExpiredRequest_Returns204AndRemovesTheRequest()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_reject_expired");
        var expired = await SeedRequestAsync(Email("regadmin_reject_expired_requester"), factory.Clock.UtcNow - Lifetime - OneSecond);

        var response = await admin.PostAsync(RejectEndpoint(expired.Id), null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await RequestStoredAsync(expired.Id));
    }

    // --- AC6: audited with target RegistrationRequest + id, in the platform admin's audit list ---

    [Fact]
    public async Task Approve_Succeeded_IsAuditedWithTheRequestAsTarget()
    {
        var (admin, adminId) = await CreatePlatformAdminWithIdAsync("regadmin_audit_approve_ok");
        var requestId = await RegisterAsync(Email("regadmin_audit_approve_ok_requester"));

        await admin.PostAsync(ApproveEndpoint(requestId), null);

        var record = Assert.Single(await RecordsOfAsync(adminId, ApproveRegistrationRequestAction));
        AssertRecord(record, requestId, SucceededOutcome, null);
    }

    [Fact]
    public async Task Approve_UnknownId_IsAuditedAsRejectedWithNotFound()
    {
        var (admin, adminId) = await CreatePlatformAdminWithIdAsync("regadmin_audit_approve_unknown");
        var unknownId = Guid.NewGuid();

        await admin.PostAsync(ApproveEndpoint(unknownId), null);

        var record = Assert.Single(await RecordsOfAsync(adminId, ApproveRegistrationRequestAction));
        AssertRecord(record, unknownId, RejectedOutcome, AdminErrorCodes.RegistrationRequestNotFound);
    }

    [Fact]
    public async Task Approve_EmailAlreadyHasAnAccount_IsAuditedAsRejectedWithEmailAlreadyInUse()
    {
        var (admin, adminId) = await CreatePlatformAdminWithIdAsync("regadmin_audit_approve_taken");
        var email = Email("regadmin_audit_approve_taken_requester");
        var requestId = await RegisterAsync(email);
        await TestUsers.CreateAsync(factory, email);

        await admin.PostAsync(ApproveEndpoint(requestId), null);

        var record = Assert.Single(await RecordsOfAsync(adminId, ApproveRegistrationRequestAction));
        AssertRecord(record, requestId, RejectedOutcome, AuthErrorCodes.EmailAlreadyInUse);
    }

    [Fact]
    public async Task Reject_Succeeded_IsAuditedWithTheRequestAsTarget()
    {
        var (admin, adminId) = await CreatePlatformAdminWithIdAsync("regadmin_audit_reject_ok");
        var requestId = await RegisterAsync(Email("regadmin_audit_reject_ok_requester"));

        await admin.PostAsync(RejectEndpoint(requestId), null);

        var record = Assert.Single(await RecordsOfAsync(adminId, RejectRegistrationRequestAction));
        AssertRecord(record, requestId, SucceededOutcome, null);
    }

    [Fact]
    public async Task Reject_UnknownId_IsAuditedAsRejectedWithNotFound()
    {
        var (admin, adminId) = await CreatePlatformAdminWithIdAsync("regadmin_audit_reject_unknown");
        var unknownId = Guid.NewGuid();

        await admin.PostAsync(RejectEndpoint(unknownId), null);

        var record = Assert.Single(await RecordsOfAsync(adminId, RejectRegistrationRequestAction));
        AssertRecord(record, unknownId, RejectedOutcome, AdminErrorCodes.RegistrationRequestNotFound);
    }

    [Fact]
    public async Task ApproveAndReject_AppearInThePlatformAdminsAuditList()
    {
        var (admin, adminId) = await CreatePlatformAdminWithIdAsync("regadmin_audit_list");
        var approvedId = await RegisterAsync(Email("regadmin_audit_list_approved"));
        var rejectedId = await RegisterAsync(Email("regadmin_audit_list_rejected"));
        (await admin.PostAsync(ApproveEndpoint(approvedId), null)).EnsureSuccessStatusCode();
        (await admin.PostAsync(RejectEndpoint(rejectedId), null)).EnsureSuccessStatusCode();

        var listed = await ReadAsync(await admin.GetAsync(PlatformAuditRecordsEndpoint + Filters(userId: adminId)));

        var actual = listed
            .Where(r => r.Action is ApproveRegistrationRequestAction or RejectRegistrationRequestAction)
            .Select(r => (r.Action, r.TargetType, r.TargetId, r.Outcome))
            .OrderBy(r => r.Action).ToList();
        Assert.Equal(
            [
                (ApproveRegistrationRequestAction, (string?)AuditTargetTypes.RegistrationRequest, (Guid?)approvedId, SucceededOutcome),
                (RejectRegistrationRequestAction, (string?)AuditTargetTypes.RegistrationRequest, (Guid?)rejectedId, SucceededOutcome)
            ],
            actual);
    }

    [Fact]
    public async Task ApproveAndReject_AuditRecordsHoldNoEmailOrHash()
    {
        var (admin, adminId) = await CreatePlatformAdminWithIdAsync("regadmin_audit_noemail");
        var approvedEmail = Email("regadmin_audit_noemail_approved");
        var rejectedEmail = Email("regadmin_audit_noemail_rejected");
        var approvedId = await RegisterAsync(approvedEmail);
        var rejectedId = await RegisterAsync(rejectedEmail);
        string[] secrets = [approvedEmail, rejectedEmail, await StoredHashAsync(approvedId), await StoredHashAsync(rejectedId)];

        await admin.PostAsync(ApproveEndpoint(approvedId), null);
        await admin.PostAsync(RejectEndpoint(rejectedId), null);

        var records = await RecordsOfAsync(adminId);
        Assert.Equal(2, records.Count);
        Assert.All(records, r => AssertHoldsNone(r, secrets));
    }

    [Fact]
    public async Task ListApproveAndReject_LogNoEmailOrHash()
    {
        var admin = await CreatePlatformAdminAsync("regadmin_logs");
        var approvedEmail = Email("regadmin_logs_approved");
        var rejectedEmail = Email("regadmin_logs_rejected");
        var takenEmail = Email("regadmin_logs_taken");
        var approvedId = await RegisterAsync(approvedEmail);
        var rejectedId = await RegisterAsync(rejectedEmail);
        var takenId = await RegisterAsync(takenEmail);
        await TestUsers.CreateAsync(factory, takenEmail);
        string[] secrets =
        [
            approvedEmail, rejectedEmail, takenEmail,
            await StoredHashAsync(approvedId), await StoredHashAsync(rejectedId), await StoredHashAsync(takenId)
        ];
        var linesBefore = factory.LogLines.Count;

        await admin.GetAsync(RequestsEndpoint);
        await admin.PostAsync(ApproveEndpoint(approvedId), null);
        await admin.PostAsync(ApproveEndpoint(takenId), null);
        await admin.PostAsync(RejectEndpoint(rejectedId), null);
        await admin.PostAsync(ApproveEndpoint(Guid.NewGuid()), null);

        var lines = factory.LogLines.Skip(linesBefore).ToList();
        Assert.All(secrets, secret => Assert.DoesNotContain(lines, line => line.Contains(secret, StringComparison.OrdinalIgnoreCase)));
    }

    // --- helpers ---

    private static string Email(string name) => $"{name}{EmailDomain}".ToLowerInvariant();

    private static string ApproveEndpoint(Guid id) => $"{RequestsEndpoint}/{id}/{ApproveRoute}";

    private static string RejectEndpoint(Guid id) => $"{RequestsEndpoint}/{id}/{RejectRoute}";

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string route) =>
        method == GetMethod ? client.GetAsync(route) : client.PostAsync(route, null);

    private async Task<HttpClient> CreatePlatformAdminAsync(string name) =>
        (await CreatePlatformAdminWithIdAsync(name)).Client;

    private async Task<(HttpClient Client, Guid UserId)> CreatePlatformAdminWithIdAsync(string name)
    {
        var email = Email($"{name}_platform");
        var client = await TestUsers.CreateClientAsync(factory, email, UserRoles.Admin);
        return (client, await TestUsers.GetIdAsync(factory, email));
    }

    private Task<HttpResponseMessage> PostRegisterAsync(string email) =>
        factory.CreateClient().PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password });

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        factory.CreateClient().PostAsJsonAsync(LoginEndpoint, new { Email = email, Password = password });

    // Submits a registration request through the real register endpoint (202) and returns its id
    private async Task<Guid> RegisterAsync(string email)
    {
        var response = await PostRegisterAsync(email);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RegisterResponse>())!.RegistrationRequestId;
    }

    // Stores a request made at the given UTC time, through the domain and the repository
    private async Task<RegistrationRequest> SeedRequestAsync(string email, DateTime requestedAt)
    {
        using var scope = factory.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var request = RegistrationRequest.Submit(email, hasher.HashPassword(Password), requestedAt);
        scope.ServiceProvider.GetRequiredService<IRegistrationRequestRepository>().Add(request);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return request;
    }

    private async Task<List<SummaryResponse>> ListAsync(HttpClient admin)
    {
        var response = await admin.GetAsync(RequestsEndpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<SummaryResponse>>())!;
    }

    private async Task<bool> RequestStoredAsync(Guid requestId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().RegistrationRequests.AnyAsync(r => r.Id == requestId);
    }

    private async Task<string> StoredHashAsync(Guid requestId)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<DContext>().RegistrationRequests.AsNoTracking()
            .SingleAsync(r => r.Id == requestId)).HashedPassword;
    }

    private async Task<List<DbUser>> StoredUsersAsync(string email)
    {
        var normalized = EmailAddress.From(email).Value;
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().Users.AsNoTracking()
            .Where(u => u.Email == normalized).ToListAsync();
    }

    private async Task<int> UserCountAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().Users.CountAsync();
    }

    // The approve and reject records of one actor (each test has its own platform admin)
    private async Task<List<DbAuditRecord>> RecordsOfAsync(Guid actorId, string? action = null)
    {
        string[] actions = action is null ? [ApproveRegistrationRequestAction, RejectRegistrationRequestAction] : [action];
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().AuditRecords.AsNoTracking()
            .Where(r => r.ActorUserId == actorId && actions.Contains(r.Action)).ToListAsync();
    }

    private static void AssertRecord(DbAuditRecord record, Guid targetId, string outcome, string? errorCode) =>
        Assert.Equal(
            ((string?)AuditTargetTypes.RegistrationRequest, (Guid?)targetId, outcome, errorCode, (Guid?)null),
            (record.TargetType, record.TargetId, record.Outcome, record.ErrorCode, record.OrganizationId));

    // No text column of the record holds any of the given values
    private static void AssertHoldsNone(DbAuditRecord record, string[] values)
    {
        string?[] texts = [record.Action, record.TargetType, record.Outcome, record.ErrorCode];
        Assert.All(values, value =>
            Assert.All(texts, text => Assert.DoesNotContain(value, text ?? string.Empty, StringComparison.OrdinalIgnoreCase)));
    }

    private record SummaryResponse(Guid Id, string Email, DateTime RequestedAt, DateTime ExpiresAt);
    private record ApproveResponse(Guid UserId);
    private record RegisterResponse(Guid RegistrationRequestId);
    private record LoginResponse(string Token);
}

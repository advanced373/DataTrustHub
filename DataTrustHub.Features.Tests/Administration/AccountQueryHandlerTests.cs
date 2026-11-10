using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.ReadModels.Memberships;
using DataTrustHub.Domain.ReadModels.Registrations;
using DataTrustHub.Domain.ReadModels.Users;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Registration;
using DataTrustHub.Features.Administration.FindUserByEmail;
using DataTrustHub.Features.Administration.GetAccount;
using DataTrustHub.Features.Administration.ListAccounts;
using DataTrustHub.Features.Administration.ListRegistrationRequests;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.SharedKernel;
using Microsoft.Extensions.Options;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

// #74 part 4d, AC3 and AC4 on the handlers: the account, user lookup and registration request queries map the read
// side's answer to their responses unchanged; find user by email checks the organization admin before any read,
// passes the normalized address (U7) and sets the found user as the audit target; the registration request list
// purges the expired requests before it reads (R2) and computes ExpiresAt in UTC. Built directly with fakes, so no
// database is involved
public class AccountQueryHandlerTests
{
    private const string AccountEmail = "account_handler@example.com";
    private const string OtherAccountEmail = "z_account_handler@example.com";
    private const string FoundEmail = "find_handler_target@example.com";
    private const string FoundEmailVariant = "\t Find_Handler_Target@Example.COM \n";
    private const string RequestEmail = "request_handler@example.com";
    private const string OtherRequestEmail = "other_request_handler@example.com";
    private const int LifetimeDays = 7;

    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid OtherOrganizationId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(LifetimeDays);

    private readonly FakeMembershipRepository _memberships = new();
    private readonly RecordingUserQueries _userQueries = new();
    private readonly RecordingMembershipQueries _membershipQueries = new();
    private readonly AuditScope _auditScope = new();
    private readonly FakeRegistrationRequestRepository _registrationRequests = new();
    private readonly RecordingRegistrationRequestQueries _registrationRequestQueries;

    public AccountQueryHandlerTests()
    {
        _registrationRequestQueries = new RecordingRegistrationRequestQueries(() => _registrationRequests.PurgeCutoffs.Count);
    }

    public enum CallerKind
    {
        WithoutMembership,
        Member,
        Guest,
        OrgAdminOfAnotherOrganization
    }

    public static TheoryData<CallerKind> NonOrgAdminCallers() =>
        new(CallerKind.WithoutMembership, CallerKind.Member, CallerKind.Guest, CallerKind.OrgAdminOfAnotherOrganization);

    // --- GetAccount ---

    [Fact]
    public async Task GetAccount_ReadSideFindsTheAccount_ReturnsItsIdEmailAndRole()
    {
        var account = new AccountEntry(Guid.NewGuid(), AccountEmail, UserRoles.User);
        _userQueries.Account = account;

        var result = await new GetAccountHandler(_userQueries).Handle(new GetAccountQuery(account.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(new GetAccountResponse(account.Id, AccountEmail, UserRoles.User), result.Value);
        Assert.Equal(account.Id, Assert.Single(_userQueries.AccountFinds));
    }

    [Fact]
    public async Task GetAccount_ReadSideFindsNothing_FailsWithAccountNotFound()
    {
        var result = await new GetAccountHandler(_userQueries).Handle(new GetAccountQuery(Guid.NewGuid()), default);

        AssertNotFound(result, AdminErrorCodes.AccountNotFound);
    }

    // --- ListAccounts ---

    [Fact]
    public async Task ListAccounts_ReturnsTheReadSideAccountsInTheirOrder()
    {
        var first = new AccountEntry(Guid.NewGuid(), AccountEmail, UserRoles.Admin);
        var second = new AccountEntry(Guid.NewGuid(), OtherAccountEmail, UserRoles.User);
        _userQueries.Accounts = [first, second];

        var result = await new ListAccountsHandler(_userQueries).Handle(new ListAccountsQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [new AccountSummary(first.Id, first.Email, first.Role), new AccountSummary(second.Id, second.Email, second.Role)],
            result.Value);
    }

    [Fact]
    public async Task ListAccounts_ReadSideHasNoAccounts_ReturnsEmpty()
    {
        var result = await new ListAccountsHandler(_userQueries).Handle(new ListAccountsQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    // --- FindUserByEmail: the organization admin check comes before any read ---

    [Theory]
    [MemberData(nameof(NonOrgAdminCallers))]
    public async Task FindUserByEmail_CallerNotOrgAdmin_FailsWithOrganizationNotFoundWithoutReading(CallerKind kind)
    {
        _userQueries.UserByEmail = new UserByEmail(Guid.NewGuid(), FoundEmail);

        var result = await FindUserByEmailHandler().Handle(
            new FindUserByEmailQuery(OrganizationId, Caller(kind), FoundEmail), default);

        AssertNotFound(result, AdminErrorCodes.OrganizationNotFound);
        Assert.Equal(0, _userQueries.Calls);
        Assert.Equal(0, _membershipQueries.Calls);
        Assert.Null(_auditScope.Target);
    }

    [Fact]
    public async Task FindUserByEmail_OrgAdmin_AsksTheReadSideForTheNormalizedAddress()
    {
        await FindUserByEmailHandler().Handle(new FindUserByEmailQuery(OrganizationId, OrgAdmin(), FoundEmailVariant), default);

        Assert.Equal(FoundEmail, Assert.Single(_userQueries.EmailFinds));
    }

    [Fact]
    public async Task FindUserByEmail_OrgAdmin_UserNotFound_FailsWithUserNotFoundWithoutTargetOrRoleLookup()
    {
        var result = await FindUserByEmailHandler().Handle(
            new FindUserByEmailQuery(OrganizationId, OrgAdmin(), FoundEmail), default);

        AssertNotFound(result, AdminErrorCodes.UserNotFound);
        Assert.Null(_auditScope.Target);
        Assert.Equal(0, _membershipQueries.Calls);
    }

    [Fact]
    public async Task FindUserByEmail_OrgAdmin_UserFound_SetsTheUserAsTheAuditTarget()
    {
        var user = new UserByEmail(Guid.NewGuid(), FoundEmail);
        _userQueries.UserByEmail = user;

        await FindUserByEmailHandler().Handle(new FindUserByEmailQuery(OrganizationId, OrgAdmin(), FoundEmail), default);

        Assert.Equal(new AuditTarget(AuditTargetTypes.User, user.Id), _auditScope.Target);
    }

    [Fact]
    public async Task FindUserByEmail_OrgAdmin_UserWithoutMembership_ReturnsNotAMemberAndNoRole()
    {
        var user = new UserByEmail(Guid.NewGuid(), FoundEmail);
        _userQueries.UserByEmail = user;

        var result = await FindUserByEmailHandler().Handle(
            new FindUserByEmailQuery(OrganizationId, OrgAdmin(), FoundEmail), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(new FindUserByEmailResponse(user.Id, FoundEmail, false, null), result.Value);
        Assert.Equal((user.Id, OrganizationId), Assert.Single(_membershipQueries.RoleFinds));
    }

    [Theory]
    [InlineData(nameof(MembershipRole.Member))]
    [InlineData(nameof(MembershipRole.Guest))]
    [InlineData(nameof(MembershipRole.OrgAdmin))]
    public async Task FindUserByEmail_OrgAdmin_UserWithMembership_ReturnsAMemberWithTheReadSideRole(string roleProperty)
    {
        var role = RoleName(roleProperty);
        var user = new UserByEmail(Guid.NewGuid(), FoundEmail);
        _userQueries.UserByEmail = user;
        _membershipQueries.Role = role;

        var result = await FindUserByEmailHandler().Handle(
            new FindUserByEmailQuery(OrganizationId, OrgAdmin(), FoundEmail), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(new FindUserByEmailResponse(user.Id, FoundEmail, true, role), result.Value);
    }

    // --- ListRegistrationRequests ---

    [Fact]
    public async Task ListRegistrationRequests_PurgesTheExpiredRequestsBeforeReading()
    {
        await ListRegistrationRequestsHandler().Handle(new ListRegistrationRequestsQuery(), default);

        Assert.Equal(Now.UtcDateTime - Lifetime, Assert.Single(_registrationRequests.PurgeCutoffs));
        Assert.Equal(1, Assert.Single(_registrationRequestQueries.PurgesSeenAtRead));
    }

    [Fact]
    public async Task ListRegistrationRequests_ReturnsTheReadSideRequestsInTheirOrderWithExpiresAtInUtc()
    {
        var olderAt = DateTime.SpecifyKind(Now.UtcDateTime.AddDays(-2), DateTimeKind.Unspecified);
        var newerAt = DateTime.SpecifyKind(Now.UtcDateTime.AddDays(-1), DateTimeKind.Unspecified);
        var older = new PendingRegistrationRequest(Guid.NewGuid(), RequestEmail, olderAt);
        var newer = new PendingRegistrationRequest(Guid.NewGuid(), OtherRequestEmail, newerAt);
        _registrationRequestQueries.Pending = [older, newer];

        var result = await ListRegistrationRequestsHandler().Handle(new ListRegistrationRequestsQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [
                new RegistrationRequestSummary(older.Id, RequestEmail, olderAt, olderAt + Lifetime),
                new RegistrationRequestSummary(newer.Id, OtherRequestEmail, newerAt, newerAt + Lifetime)
            ],
            result.Value);
        Assert.All(result.Value, summary =>
        {
            Assert.Equal(DateTimeKind.Utc, summary.RequestedAt.Kind);
            Assert.Equal(DateTimeKind.Utc, summary.ExpiresAt.Kind);
        });
    }

    // --- helpers ---

    private FindUserByEmailHandler FindUserByEmailHandler() =>
        new(_memberships, _userQueries, _membershipQueries, _auditScope);

    private ListRegistrationRequestsHandler ListRegistrationRequestsHandler() =>
        new(
            _registrationRequests,
            _registrationRequestQueries,
            Options.Create(new RegistrationOptions { PendingRequestLifetimeDays = LifetimeDays }),
            new TestClock(Now));

    private Guid OrgAdmin() => _memberships.Seed(Membership.AppointOrgAdmin(Guid.NewGuid(), OrganizationId)).UserId;

    private Guid Caller(CallerKind kind) => kind switch
    {
        CallerKind.WithoutMembership => Guid.NewGuid(),
        CallerKind.Member => _memberships.Seed(Membership.AddMember(Guid.NewGuid(), OrganizationId)).UserId,
        CallerKind.Guest => _memberships.Seed(Membership.AddGuest(Guid.NewGuid(), OrganizationId)).UserId,
        CallerKind.OrgAdminOfAnotherOrganization =>
            _memberships.Seed(Membership.AppointOrgAdmin(Guid.NewGuid(), OtherOrganizationId)).UserId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string RoleName(string roleProperty) => roleProperty switch
    {
        nameof(MembershipRole.Member) => MembershipRole.Member.Name,
        nameof(MembershipRole.Guest) => MembershipRole.Guest.Name,
        nameof(MembershipRole.OrgAdmin) => MembershipRole.OrgAdmin.Name,
        _ => throw new ArgumentOutOfRangeException(nameof(roleProperty))
    };

    private static void AssertNotFound<T>(Result<T> result, string code)
    {
        Assert.True(result.IsFailure);
        Assert.Equal((code, ErrorType.NotFound), (result.Error.Code, result.Error.Type));
    }

    // Records every read, so a test can prove what the handler asked for and that it did not read before its check
    private sealed class RecordingUserQueries : IUserQueries
    {
        internal IReadOnlyList<AccountEntry> Accounts { get; set; } = [];
        internal AccountEntry? Account { get; set; }
        internal UserByEmail? UserByEmail { get; set; }
        internal List<Guid> AccountFinds { get; } = [];
        internal List<string> EmailFinds { get; } = [];
        internal int Lists { get; private set; }
        internal int Calls => Lists + AccountFinds.Count + EmailFinds.Count;

        public Task<IReadOnlyList<AccountEntry>> ListAccountsAsync(CancellationToken cancellationToken)
        {
            Lists++;
            return Task.FromResult(Accounts);
        }

        public Task<AccountEntry?> FindAccountAsync(Guid userId, CancellationToken cancellationToken)
        {
            AccountFinds.Add(userId);
            return Task.FromResult(Account);
        }

        public Task<UserByEmail?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        {
            EmailFinds.Add(normalizedEmail);
            return Task.FromResult(UserByEmail);
        }
    }

    private sealed class RecordingMembershipQueries : IMembershipQueries
    {
        internal string? Role { get; set; }
        internal List<(Guid UserId, Guid OrganizationId)> RoleFinds { get; } = [];
        internal int Calls { get; private set; }

        public Task<IReadOnlyList<OrganizationMember>> ListMembersAsync(Guid organizationId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<OrganizationMember>>([]);
        }

        public Task<IReadOnlyList<MyOrganization>> ListMyOrganizationsAsync(Guid userId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<MyOrganization>>([]);
        }

        public Task<string?> FindRoleAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken)
        {
            Calls++;
            RoleFinds.Add((userId, organizationId));
            return Task.FromResult(Role);
        }
    }

    // Notes, at each read, how many purges the repository had seen, so a test can prove the purge came first
    private sealed class RecordingRegistrationRequestQueries(Func<int> purgesSoFar) : IRegistrationRequestQueries
    {
        internal IReadOnlyList<PendingRegistrationRequest> Pending { get; set; } = [];
        internal List<int> PurgesSeenAtRead { get; } = [];

        public Task<IReadOnlyList<PendingRegistrationRequest>> ListPendingAsync(CancellationToken cancellationToken)
        {
            PurgesSeenAtRead.Add(purgesSoFar());
            return Task.FromResult(Pending);
        }
    }
}

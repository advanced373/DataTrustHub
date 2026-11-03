using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features._Shared.RateLimiting;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// #122 Part A (ADR-0025): find-by-email is limited to 10 calls per caller per minute (AC1), the budget is the
// caller's own (AC2) and covers all their organizations, and no other endpoint is limited (AC3). The limiter
// keeps its state in the app, partitioned by caller id: every test uses callers of its own, so no test spends
// another test's budget
public class FindUserByEmailRateLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int PermitLimit = 10;
    private const int CallsBeyondLimit = PermitLimit + 5;
    private const string EmailDomain = "@example.com";
    private const string RegisterEndpoint = "/auth/register";
    private const string Password = "Password1!";
    private const string FindUserByEmailAction = "FindUserByEmail";
    private const string FindUserByEmailRoutePattern = "organizations/{organizationId:guid}/users/find-by-email";
    private const char PathSeparator = '/';

    // --- AC1: the 11th call of a caller within one minute ---

    [Fact]
    public async Task FindUserByEmail_FirstTenCallsFound_EachReturns200()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "limit_first_ten");
        await CreateUserAsync(factory, "limit_first_ten_target");

        var statuses = await LookupTimesAsync(scenario.OrgAdmin, scenario.OrganizationId, $"limit_first_ten_target{EmailDomain}", PermitLimit);

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.OK, status));
    }

    [Fact]
    public async Task FindUserByEmail_EleventhCall_Returns429()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "limit_eleventh");
        var email = $"limit_eleventh_orgadmin{EmailDomain}";
        await LookupTimesAsync(scenario.OrgAdmin, scenario.OrganizationId, email, PermitLimit);

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, email);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task FindUserByEmail_CallsBeyondTheLimit_AllReturn429()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "limit_beyond");
        var email = $"limit_beyond_orgadmin{EmailDomain}";
        await LookupTimesAsync(scenario.OrgAdmin, scenario.OrganizationId, email, PermitLimit);

        var statuses = await LookupTimesAsync(scenario.OrgAdmin, scenario.OrganizationId, email, CallsBeyondLimit - PermitLimit);

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.TooManyRequests, status));
    }

    // Rejected lookups (user not found, invalid input) spend the budget as well: calls 1-10 answer as before
    [Fact]
    public async Task FindUserByEmail_TenRejectedLookups_AnswerAsBeforeThenTheEleventhReturns429()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "limit_rejected");

        var notFound = await LookupTimesAsync(scenario.OrgAdmin, scenario.OrganizationId, $"limit_rejected_nobody{EmailDomain}", PermitLimit - 1);
        var invalid = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, "not-an-email");
        var eleventh = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, $"limit_rejected_nobody{EmailDomain}");

        Assert.All(notFound, status => Assert.Equal(HttpStatusCode.NotFound, status));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
    }

    // The rejection says nothing about the user searched for
    [Fact]
    public async Task FindUserByEmail_Rejected429_BodyHoldsNoUserDetail()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "limit_body");
        var (_, userId) = await CreateUserAsync(factory, "limit_body_target");
        var email = $"limit_body_target{EmailDomain}";
        await LookupTimesAsync(scenario.OrgAdmin, scenario.OrganizationId, email, PermitLimit);

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, email);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.DoesNotContain(email, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(userId.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    // A request the rate limiter rejects never reaches the handler, so it leaves no audit record
    [Fact]
    public async Task FindUserByEmail_Rejected429_AppendsNoAuditRecord()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "limit_no_record");
        var email = $"limit_no_record_orgadmin{EmailDomain}";
        await LookupTimesAsync(scenario.OrgAdmin, scenario.OrganizationId, email, PermitLimit);
        var before = await LookupRecordCountAsync(scenario.OrgAdminId);

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, email);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal((PermitLimit, PermitLimit), (before, await LookupRecordCountAsync(scenario.OrgAdminId)));
    }

    // The endpoint requires authentication before the limiter runs: an anonymous caller keeps getting 401
    [Fact]
    public async Task FindUserByEmail_AnonymousBeyondTheLimit_KeepsReturning401()
    {
        var anonymous = factory.CreateClient();

        var statuses = await LookupTimesAsync(anonymous, Guid.NewGuid(), $"limit_anonymous{EmailDomain}", CallsBeyondLimit);

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.Unauthorized, status));
    }

    // --- AC2: another caller ---

    [Fact]
    public async Task FindUserByEmail_AnotherCallerAfterTheFirstIsLimited_Returns200()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "limit_other_caller");
        var (secondOrgAdmin, _) = await OrganizationCallers.AddSecondOrgAdminAsync(factory, scenario, "limit_other_caller_second");
        var email = $"limit_other_caller_orgadmin{EmailDomain}";
        await LookupTimesAsync(scenario.OrgAdmin, scenario.OrganizationId, email, PermitLimit + 1);

        var response = await PostFindUserByEmailAsync(secondOrgAdmin, scenario.OrganizationId, email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FindUserByEmail_AnotherCaller_HasAFullBudgetOfItsOwn()
    {
        var first = await CreateOrganizationWithOrgAdminAsync(factory, "limit_own_budget_first");
        var second = await CreateOrganizationWithOrgAdminAsync(factory, "limit_own_budget_second");
        await LookupTimesAsync(first.OrgAdmin, first.OrganizationId, $"limit_own_budget_first_orgadmin{EmailDomain}", PermitLimit + 1);

        var statuses = await LookupTimesAsync(second.OrgAdmin, second.OrganizationId, $"limit_own_budget_second_orgadmin{EmailDomain}", PermitLimit);

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.OK, status));
    }

    // Part A: the limit applies per caller, not per organization
    [Fact]
    public async Task FindUserByEmail_OrgAdminOfTwoOrganizations_SharesOneBudget()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "limit_two_orgs");
        var (platformAdmin, otherOrganizationId) = await CreateOrganizationWithoutMembersAsync(factory, "limit_two_orgs_other");
        await AppointOrganizationAdminAsync(platformAdmin, otherOrganizationId, scenario.OrgAdminId);
        var email = $"limit_two_orgs_orgadmin{EmailDomain}";
        await LookupTimesAsync(scenario.OrgAdmin, scenario.OrganizationId, email, PermitLimit);

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, otherOrganizationId, email);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    // --- AC3: other endpoints ---

    [Fact]
    public async Task OtherEndpoints_CallerLimitedOnFindUserByEmail_KeepAnswering()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "limit_others_after");
        await LookupTimesAsync(scenario.OrgAdmin, scenario.OrganizationId, $"limit_others_after_orgadmin{EmailDomain}", PermitLimit + 1);

        var members = await scenario.OrgAdmin.GetAsync(MembersEndpoint(scenario.OrganizationId));
        var mine = await scenario.OrgAdmin.GetAsync(MyOrganizationsEndpoint);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (members.StatusCode, mine.StatusCode));
    }

    [Fact]
    public async Task OtherEndpoints_CalledMoreThanTheLimit_NeverReturn429()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "limit_others_many");
        var statuses = new List<HttpStatusCode>();

        for (var call = 0; call < CallsBeyondLimit; call++)
        {
            statuses.Add((await scenario.OrgAdmin.GetAsync(MembersEndpoint(scenario.OrganizationId))).StatusCode);
            statuses.Add((await scenario.OrgAdmin.GetAsync(MyOrganizationsEndpoint)).StatusCode);
            statuses.Add((await PostMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, Guid.NewGuid(), MemberRole)).StatusCode);
        }

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task Register_CalledMoreThanTheLimitAnonymously_NeverReturns429()
    {
        var anonymous = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();

        for (var call = 0; call < CallsBeyondLimit; call++)
        {
            var email = $"limit_register_{Guid.NewGuid():N}{EmailDomain}";
            statuses.Add((await anonymous.PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password })).StatusCode);
        }

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public void RateLimiter_HasNoGlobalLimiter()
    {
        var options = factory.Services.GetRequiredService<IOptions<RateLimiterOptions>>().Value;

        Assert.Null(options.GlobalLimiter);
    }

    [Fact]
    public void Endpoints_OnlyFindUserByEmailRequiresARateLimit()
    {
        var limited = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<EnableRateLimitingAttribute>() is not null)
            .ToList();

        var endpoint = Assert.Single(limited);
        Assert.Equal(FindUserByEmailRoutePattern, endpoint.RoutePattern.RawText!.Trim(PathSeparator));
        Assert.Equal(RateLimitPolicies.UserLookup, endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()!.PolicyName);
    }

    // --- helpers ---

    private static async Task<List<HttpStatusCode>> LookupTimesAsync(HttpClient caller, Guid organizationId, string email, int times)
    {
        var statuses = new List<HttpStatusCode>();
        for (var call = 0; call < times; call++)
            statuses.Add((await PostFindUserByEmailAsync(caller, organizationId, email)).StatusCode);
        return statuses;
    }

    private async Task<int> LookupRecordCountAsync(Guid actorUserId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().AuditRecords.AsNoTracking()
            .CountAsync(r => r.ActorUserId == actorUserId && r.Action == FindUserByEmailAction);
    }
}

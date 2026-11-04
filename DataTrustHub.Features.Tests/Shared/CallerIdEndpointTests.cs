using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Shared.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Shared;

// AC3 of #119: every endpoint that reads the caller id answers exactly as before when the id is missing or
// malformed: 401 with an empty body, from the endpoint itself (the caller is an authenticated platform admin, so
// the route's authorization passes). All 23 endpoints that read the caller id are covered, not a sample: each is
// one row below, so no endpoint is left to an argument that it behaves like another
public abstract class CallerIdEndpointTests<TFactory>(TFactory factory) : IClassFixture<TFactory>
    where TFactory : CallerClaimsApiFactory
{
    private const string AccountsEndpoint = "/admin/accounts";
    private const string MemberRole = "Member";
    private const string PolicyName = "Caller id policy";
    private const string ClearanceName = "Caller id clearance";
    private const string LevelName = "Secret";
    private const int LevelPriority = 3;
    private const string TargetEmail = "callerid_target@example.com";
    private const string PolicyIdField = "policyId";

    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid OtherId = Guid.NewGuid();

    private static readonly Dictionary<string, Func<HttpRequestMessage>> Requests = new()
    {
        // Administration
        ["AddMember"] = () => Json(HttpMethod.Post, $"/organizations/{OrganizationId}/members", new { UserId = OtherId, Role = MemberRole }),
        ["AppointOrganizationAdmin"] = () => Json(HttpMethod.Post, $"/admin/organizations/{OrganizationId}/admins", new { UserId = OtherId }),
        ["ChangeMemberRole"] = () => Json(HttpMethod.Put, $"/organizations/{OrganizationId}/members/{OtherId}/role", new { Role = MemberRole }),
        ["CreateClearance"] = () => Json(HttpMethod.Post, $"/organizations/{OrganizationId}/clearances", new
        {
            UserId = OtherId, PolicyId = OtherId, Name = ClearanceName,
            ClassificationLevelName = LevelName, ClassificationLevelPriority = LevelPriority
        }),
        ["CreatePolicy"] = () => Json(HttpMethod.Post, $"/organizations/{OrganizationId}/policies", new { Name = PolicyName }),
        ["DeleteAccount"] = () => new HttpRequestMessage(HttpMethod.Delete, $"/admin/accounts/{OtherId}"),
        ["DeleteClearance"] = () => new HttpRequestMessage(HttpMethod.Delete, $"/organizations/{OrganizationId}/clearances/{OtherId}"),
        ["DeletePolicy"] = () => new HttpRequestMessage(HttpMethod.Delete, $"/organizations/{OrganizationId}/policies/{OtherId}"),
        ["FindUserByEmail"] = () => Json(HttpMethod.Post, $"/organizations/{OrganizationId}/users/find-by-email", new { Email = TargetEmail }),
        ["GetClearance"] = () => new HttpRequestMessage(HttpMethod.Get, $"/organizations/{OrganizationId}/clearances/{OtherId}"),
        ["GetPolicy"] = () => new HttpRequestMessage(HttpMethod.Get, $"/organizations/{OrganizationId}/policies/{OtherId}"),
        ["LeaveOrganization"] = () => new HttpRequestMessage(HttpMethod.Delete, $"/organizations/{OrganizationId}/membership"),
        ["ListClearances"] = () => new HttpRequestMessage(HttpMethod.Get, $"/organizations/{OrganizationId}/clearances"),
        ["ListMembers"] = () => new HttpRequestMessage(HttpMethod.Get, $"/organizations/{OrganizationId}/members"),
        ["ListMyOrganizations"] = () => new HttpRequestMessage(HttpMethod.Get, "/organizations/mine"),
        ["ListOrganizationAuditRecords"] = () => new HttpRequestMessage(HttpMethod.Get, $"/organizations/{OrganizationId}/audit-records"),
        ["ListPolicies"] = () => new HttpRequestMessage(HttpMethod.Get, $"/organizations/{OrganizationId}/policies"),
        ["RemoveMember"] = () => new HttpRequestMessage(HttpMethod.Delete, $"/organizations/{OrganizationId}/members/{OtherId}"),

        // DataManagement
        ["AddDataItem"] = () => new HttpRequestMessage(HttpMethod.Post, "/data/upload")
        {
            Content = new MultipartFormDataContent { { new StringContent(OtherId.ToString()), PolicyIdField } }
        },
        ["DeleteDataItem"] = () => new HttpRequestMessage(HttpMethod.Delete, $"/data/{OtherId}"),
        ["GetDataItem"] = () => new HttpRequestMessage(HttpMethod.Get, $"/data/{OtherId}"),
        ["ListClassificationOptions"] = () => new HttpRequestMessage(HttpMethod.Get, "/data/classification-options"),
        ["ListDataItems"] = () => new HttpRequestMessage(HttpMethod.Get, "/data")
    };

    public static TheoryData<string> Endpoints => new(Requests.Keys);

    // CallerIdRuleLocationTests checks that these are exactly the endpoints that read the caller id
    internal static IReadOnlyCollection<string> EndpointNames => Requests.Keys;

    // Control: the caller is authenticated and a platform admin (200 from an /admin route that does not read
    // the caller id), so a 401 below comes from the endpoint and not from the authorization middleware
    [Fact]
    public async Task Caller_IsAuthenticatedPlatformAdmin()
    {
        var response = await factory.CreateClient().GetAsync(AccountsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_CallerIdNotUsable_Returns401WithEmptyBody(string endpoint)
    {
        var response = await factory.CreateClient().SendAsync(Requests[endpoint]());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    private static HttpRequestMessage Json(HttpMethod method, string url, object body) =>
        new(method, url) { Content = JsonContent.Create(body) };
}

// AC3 of #119: no sub and no NameIdentifier claim
public class MissingCallerIdEndpointTests(MissingCallerIdApiFactory factory)
    : CallerIdEndpointTests<MissingCallerIdApiFactory>(factory);

// AC3 of #119: a sub that is not a Guid next to a NameIdentifier that is one; the old rule gave no caller id
public class MalformedCallerIdEndpointTests(MalformedCallerIdApiFactory factory)
    : CallerIdEndpointTests<MalformedCallerIdApiFactory>(factory);

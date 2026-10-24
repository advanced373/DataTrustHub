using System.Net;
using DataTrustHub.Features.Tests.Administration.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// AC1 and AC5 of #95 step 2a, AC8 of step 2b, AC1 of step 3a (policies): the caller id comes from the JWT sub claim; an authenticated caller without one
// gets 401 from the endpoint, even when they pass the route's role check
public class MembershipCallerWithoutSubjectTests(NoSubjectApiFactory factory) : IClassFixture<NoSubjectApiFactory>
{
    private const string AccountsEndpoint = "/admin/accounts";

    // Control: the caller is authenticated and a platform admin (200 from an /admin route that does not read
    // the caller id), so a 401 below comes from the endpoint
    [Fact]
    public async Task CallerWithoutSubject_IsAuthenticatedPlatformAdmin()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(AccountsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AppointOrganizationAdmin_CallerWithoutSubject_Returns401()
    {
        var client = factory.CreateClient();

        var response = await PostAppointOrganizationAdminAsync(client, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AddMember_CallerWithoutSubject_Returns401()
    {
        var client = factory.CreateClient();

        var response = await PostMemberAsync(client, Guid.NewGuid(), Guid.NewGuid(), MemberRole);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListMembers_CallerWithoutSubject_Returns401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(MembersEndpoint(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangeMemberRole_CallerWithoutSubject_Returns401()
    {
        var client = factory.CreateClient();

        var response = await PutMemberRoleAsync(client, Guid.NewGuid(), Guid.NewGuid(), MemberRole);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RemoveMember_CallerWithoutSubject_Returns401()
    {
        var client = factory.CreateClient();

        var response = await DeleteMemberAsync(client, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LeaveOrganization_CallerWithoutSubject_Returns401()
    {
        var client = factory.CreateClient();

        var response = await LeaveOrganizationAsync(client, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- policies (#95 step 3a) ---

    [Fact]
    public async Task CreatePolicy_CallerWithoutSubject_Returns401()
    {
        var client = factory.CreateClient();

        var response = await PostPolicyAsync(client, Guid.NewGuid(), "No subject policy");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListPolicies_CallerWithoutSubject_Returns401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(PoliciesEndpoint(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPolicy_CallerWithoutSubject_Returns401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(PolicyEndpoint(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeletePolicy_CallerWithoutSubject_Returns401()
    {
        var client = factory.CreateClient();

        var response = await client.DeleteAsync(PolicyEndpoint(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

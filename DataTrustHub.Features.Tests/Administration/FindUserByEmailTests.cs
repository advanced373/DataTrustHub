using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// AC2 of #95 step 5a: an organization admin finds a user by their exact email address and learns whether
// they already have a membership in this organization; every other caller gets 404 Organization.NotFound.
// The email travels in the JSON body of a POST, never in the URL (ADR-0019)
public class FindUserByEmailTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string EmailDomain = "@example.com";
    private const int MaxEmailLength = 256;
    private const string JsonMediaType = "application/json";
    private const string EmptyJsonBody = "{}";
    private const string NullEmailJsonBody = "{\"email\":null}";

    private static readonly HashSet<string> ContractFields = ["userId", "email", "isMember", "role"];

    // --- who may call ---

    public static TheoryData<string> NotOrgAdminKinds() => new(OrganizationCallers.NotOrgAdminKinds);

    [Theory]
    [MemberData(nameof(NotOrgAdminKinds))]
    public async Task FindUserByEmail_CallerNotOrgAdmin_ExistingEmail_Returns404OrganizationNotFound(string kind)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"find_deny_{kind}");
        var caller = await OrganizationCallers.CreateNotOrgAdminAsync(factory, scenario, kind, $"find_deny_{kind}");
        await CreateUserAsync(factory, $"find_deny_{kind}_target");

        var response = await PostFindUserByEmailAsync(caller, scenario.OrganizationId, $"find_deny_{kind}_target{EmailDomain}");

        await AssertOrganizationNotFoundAsync(response);
    }

    [Theory]
    [MemberData(nameof(NotOrgAdminKinds))]
    public async Task FindUserByEmail_CallerNotOrgAdmin_UnknownEmail_Returns404OrganizationNotFound(string kind)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"find_deny_unknown_{kind}");
        var caller = await OrganizationCallers.CreateNotOrgAdminAsync(factory, scenario, kind, $"find_deny_unknown_{kind}");

        var response = await PostFindUserByEmailAsync(caller, scenario.OrganizationId, $"nobody_{kind}{EmailDomain}");

        await AssertOrganizationNotFoundAsync(response);
    }

    [Fact]
    public async Task FindUserByEmail_UnknownOrganization_Returns404OrganizationNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "find_noorg");

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, Guid.NewGuid(), $"find_noorg_orgadmin{EmailDomain}");

        await AssertOrganizationNotFoundAsync(response);
    }

    [Fact]
    public async Task FindUserByEmail_Anonymous_Returns401()
    {
        var anonymous = factory.CreateClient();

        var response = await PostFindUserByEmailAsync(anonymous, Guid.NewGuid(), $"anyone{EmailDomain}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- found ---

    [Fact]
    public async Task FindUserByEmail_UserWithoutMembership_ReturnsUserNotAMember()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "find_outsider");
        var (_, userId) = await CreateUserAsync(factory, "find_outsider_target");

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, $"find_outsider_target{EmailDomain}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new FoundUser(userId, $"find_outsider_target{EmailDomain}", false, null),
            await response.Content.ReadFromJsonAsync<FoundUser>());
    }

    [Theory]
    [InlineData(MemberRole)]
    [InlineData(GuestRole)]
    public async Task FindUserByEmail_MemberOrGuest_ReturnsIsMemberWithRole(string role)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"find_found_{role}");
        var (_, userId) = await CreateUserAsync(factory, $"find_found_{role}_target");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, role);

        var found = await FindAsync(scenario, $"find_found_{role}_target{EmailDomain}");

        // The response carries the stored, normalized email (U7, #123)
        Assert.Equal(new FoundUser(userId, $"find_found_{role.ToLowerInvariant()}_target{EmailDomain}", true, role), found);
    }

    [Fact]
    public async Task FindUserByEmail_AnotherOrgAdmin_ReturnsIsMemberWithOrgAdminRole()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "find_found_orgadmin");
        var (_, userId) = await OrganizationCallers.AddSecondOrgAdminAsync(factory, scenario, "find_found_orgadmin_second");

        var found = await FindAsync(scenario, $"find_found_orgadmin_second{EmailDomain}");

        Assert.Equal(new FoundUser(userId, $"find_found_orgadmin_second{EmailDomain}", true, OrgAdminRole), found);
    }

    [Fact]
    public async Task FindUserByEmail_OwnEmail_ReturnsIsMemberWithOrgAdminRole()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "find_self");

        var found = await FindAsync(scenario, $"find_self_orgadmin{EmailDomain}");

        Assert.Equal(new FoundUser(scenario.OrgAdminId, $"find_self_orgadmin{EmailDomain}", true, OrgAdminRole), found);
    }

    [Fact]
    public async Task FindUserByEmail_MemberOfAnotherOrganizationOnly_ReturnsNotAMemberHere()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "find_elsewhere");
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "find_elsewhere_other");
        var (_, userId) = await CreateUserAsync(factory, "find_elsewhere_target");
        await AddMemberAsync(other.OrgAdmin, other.OrganizationId, userId, MemberRole);

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, $"find_elsewhere_target{EmailDomain}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(
            new FoundUser(userId, $"find_elsewhere_target{EmailDomain}", false, null),
            JsonSerializer.Deserialize<FoundUser>(body, JsonSerializerOptions.Web));
        Assert.DoesNotContain(other.OrganizationId.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FindUserByEmail_Response_HasExactlyTheFourContractFields()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "find_shape");
        await CreateUserAsync(factory, "find_shape_target");

        var fields = await ResponseFieldsAsync(scenario, $"find_shape_target{EmailDomain}");

        Assert.Equal(ContractFields, fields);
    }

    [Fact]
    public async Task FindUserByEmail_PlatformAdminFound_ReturnsNoPlatformRole()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "find_platform_target");
        var platformAdminEmail = $"find_platform_target_platform{EmailDomain}";

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, platformAdminEmail);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ContractFields, FieldsOf(body));
        Assert.Null(JsonSerializer.Deserialize<FoundUser>(body, JsonSerializerOptions.Web)!.Role);
        Assert.DoesNotContain($"\"{UserRoles.Admin}\"", body);
    }

    // --- exact match only ---

    [Theory]
    [InlineData("find_exact_target@example.co")]
    [InlineData("exact_target@example.com")]
    [InlineData("find_exact@example.com")]
    [InlineData("find_exact%@example.com")]
    [InlineData("%@%")]
    [InlineData("*@*")]
    [InlineData("find_exact*@example.com")]
    [InlineData("find_exact_target@example.co_")]
    public async Task FindUserByEmail_NotTheExactEmail_Returns404UserNotFound(string email)
    {
        var scenario = await ExactMatchScenarioAsync();

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, email);

        await AssertUserNotFoundAsync(response);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("*")]
    [InlineData("find_exact")]
    public async Task FindUserByEmail_WildcardOrPrefixWithoutAt_Returns400(string email)
    {
        var scenario = await ExactMatchScenarioAsync();

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, email);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FindUserByEmail_UnknownEmail_Returns404UserNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "find_unknown");

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, $"find_unknown_nobody{EmailDomain}");

        await AssertUserNotFoundAsync(response);
    }

    // --- validator ---

    [Theory]
    [InlineData(EmptyJsonBody)]
    [InlineData(NullEmailJsonBody)]
    public async Task FindUserByEmail_MissingOrNullEmail_Returns400(string body)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"find_missing_{Guid.NewGuid():N}");

        var response = await scenario.OrgAdmin.PostAsync(
            FindUserByEmailEndpoint(scenario.OrganizationId), new StringContent(body, Encoding.UTF8, JsonMediaType));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- the email does not travel in the URL (ADR-0019) ---

    [Fact]
    public async Task FindUserByEmail_LegacyGetWithEmailInQueryString_Returns404()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "find_legacy_get");

        var response = await scenario.OrgAdmin.GetAsync(
            LegacyFindUserByEmailEndpoint(scenario.OrganizationId, $"find_legacy_get_orgadmin{EmailDomain}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FindUserByEmail_GetOnNewRoute_DoesNotAnswer200()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "find_get_new");

        var response = await scenario.OrgAdmin.GetAsync(FindUserByEmailEndpoint(scenario.OrganizationId));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("someone@")]
    public async Task FindUserByEmail_EmptyOrMalformedEmail_Returns400(string email)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"find_malformed_{Guid.NewGuid():N}");

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, email);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FindUserByEmail_EmailLongerThanMax_Returns400()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "find_too_long");

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, EmailOfLength(MaxEmailLength + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FindUserByEmail_EmailOfMaxLength_PassesValidationAndReturns404UserNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "find_max_length");

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, EmailOfLength(MaxEmailLength));

        await AssertUserNotFoundAsync(response);
    }

    // --- helpers ---

    // A new organization admin and the target user "find_exact_target", shared by the exact-match cases.
    // Registering an email again is refused, so the target exists once whatever the order of the cases
    private async Task<OrganizationScenario> ExactMatchScenarioAsync()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"find_exact_{Guid.NewGuid():N}");
        await CreateUserAsync(factory, "find_exact_target");
        return scenario;
    }

    private async Task<FoundUser?> FindAsync(OrganizationScenario scenario, string email)
    {
        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, email);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<FoundUser>();
    }

    private static async Task<HashSet<string>> ResponseFieldsAsync(OrganizationScenario scenario, string email) =>
        FieldsOf(await (await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, email)).Content.ReadAsStringAsync());

    private static HashSet<string> FieldsOf(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
    }

    private static string EmailOfLength(int length) => new string('a', length - EmailDomain.Length) + EmailDomain;

    private static Task AssertOrganizationNotFoundAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.NotFound, AdminErrorCodes.OrganizationNotFound, AdminErrorMessages.OrganizationNotFound);

    private static Task AssertUserNotFoundAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(response, HttpStatusCode.NotFound, AdminErrorCodes.UserNotFound, AdminErrorMessages.UserNotFound);

    private record FoundUser(Guid UserId, string Email, bool IsMember, string? Role);
}

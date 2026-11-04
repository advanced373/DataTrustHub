using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// AC5 and AC7 of #123: an organization admin finds a user with any case or space variant of the address and
// the response carries the normalized email (U7); otherwise the match stays exact, and the format and length
// rules (U2) apply to the trimmed address
public class FindUserByEmailNormalizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string EmailDomain = "@example.com";
    private const int MaxEmailLength = 256;
    private const string SurroundingSpaces = "   ";
    private const string TargetName = "find_norm_target";
    private const string TargetEmail = TargetName + EmailDomain;

    // The variants themselves are pinned on EmailAddress (EmailAddressTests)
    [Fact]
    public async Task FindUserByEmail_CaseOrSpaceVariant_ReturnsTheUserWithTheNormalizedEmail()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "find_norm_variant");
        var userId = await TargetIdAsync();

        var response = await PostFindUserByEmailAsync(
            scenario.OrgAdmin, scenario.OrganizationId, "\tFind_Norm_Target@Example.Com\n");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new FoundUser(userId, TargetEmail, false, null), await response.Content.ReadFromJsonAsync<FoundUser>());
    }

    [Fact]
    public async Task FindUserByEmail_UserRegisteredWithAVariant_IsFoundWithTheNormalizedEmail()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "find_norm_registered");
        await TestUsers.CreateClientAsync(factory, " Find_Norm_Registered_Target@Example.COM ");

        var response = await PostFindUserByEmailAsync(
            scenario.OrgAdmin, scenario.OrganizationId, "find_norm_registered_target@example.com");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "find_norm_registered_target@example.com",
            (await response.Content.ReadFromJsonAsync<FoundUser>())!.Email);
    }

    // Normalization does not widen the match: a variant of another address is still not found
    [Theory]
    [InlineData(" Find_Norm_Target@Example.co ")]
    [InlineData(" %@% ")]
    public async Task FindUserByEmail_VariantOfAnotherAddress_Returns404UserNotFound(string email)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"find_norm_other_{Guid.NewGuid():N}");
        await TargetIdAsync();

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, email);

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.NotFound, AdminErrorCodes.UserNotFound, AdminErrorMessages.UserNotFound);
    }

    // --- AC7: validation on the trimmed address ---

    [Fact]
    public async Task FindUserByEmail_WhitespaceOnlyEmail_Returns400()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "find_norm_blank");

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, " \t ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FindUserByEmail_MalformedEmailWithSurroundingSpaces_Returns400()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "find_norm_malformed");

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, " not-an-email ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FindUserByEmail_TrimmedEmailOfMaxLengthWithSurroundingSpaces_PassesValidationAndReturns404UserNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "find_norm_max_length");
        var email = SurroundingSpaces + EmailOfLength(MaxEmailLength) + SurroundingSpaces;

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, email);

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.NotFound, AdminErrorCodes.UserNotFound, AdminErrorMessages.UserNotFound);
    }

    [Fact]
    public async Task FindUserByEmail_TrimmedEmailLongerThanMax_Returns400()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "find_norm_too_long");
        var email = SurroundingSpaces + EmailOfLength(MaxEmailLength + 1) + SurroundingSpaces;

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, email);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // The target is registered once per fixture; registering it again is refused, so the id stays the same
    private async Task<Guid> TargetIdAsync()
    {
        var (_, userId) = await CreateUserAsync(factory, TargetName);
        return userId;
    }

    private static string EmailOfLength(int length) => new string('a', length - EmailDomain.Length) + EmailDomain;

    private record FoundUser(Guid UserId, string Email, bool IsMember, string? Role);
}

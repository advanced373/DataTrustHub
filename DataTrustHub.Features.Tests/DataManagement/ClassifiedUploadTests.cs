using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.DataManagement;

public class ClassifiedUploadTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string UploadEndpoint = "/data/upload";
    private const string OptionsEndpoint = "/data/classification-options";
    private const int Confidential = 2;
    private const int Secret = 3;
    private const string NotClearedCode = "DataItem.NotCleared";
    private const string NotClearedMessage =
        "You can classify data only under a policy you hold a clearance on, and at most at your clearance level.";
    private const int NonStandardPriority = 9;
    private const string ValidationCode = "Validation.General";
    private const string ValidationMessage = "One or more validation errors occurred";
    private const string LevelsProperty = "levels";
    private const string PoliciesProperty = "policies";
    private const string NameProperty = "name";
    private const string PriorityProperty = "priority";
    private const string OrganizationNameProperty = "organizationName";
    private static readonly string[] PolicyOptionProperties = ["policyId", "policyName", OrganizationNameProperty, "maxPriority"];

    private static readonly (string Name, int Priority)[] StandardLevels =
        [("Public", 0), ("Internal", 1), ("Confidential", 2), ("Secret", 3), ("Top Secret", 4)];

    [Fact]
    public async Task Upload_UnderClearedPolicyAtOwnLevel_StoresPolicyAndLevel()
    {
        var (user, policyId) = await CreateClearedUserAsync("classified_ok", Confidential);

        var response = await UploadAsync(user, policyId, Confidential);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var itemId = (await response.Content.ReadFromJsonAsync<AddDataItemResponse>())!.DataItemId;
        using var scope = factory.Services.CreateScope();
        var item = await scope.ServiceProvider.GetRequiredService<DContext>().DataItems.SingleAsync(d => d.Id == itemId);
        Assert.Equal((policyId, Confidential, "Confidential"), (item.PolicyId, item.ClassificationPriority, item.SecurityMarking));
    }

    [Fact]
    public async Task Upload_AboveOwnClearanceLevel_Returns400()
    {
        var (user, policyId) = await CreateClearedUserAsync("classified_above", Confidential);

        var response = await UploadAsync(user, policyId, Secret);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNotClearedAsync(response);
    }

    [Fact]
    public async Task Upload_UnderPolicyWithoutClearance_Returns400()
    {
        var (user, _) = await CreateClearedUserAsync("classified_other", Secret);
        var admin = await TestUsers.CreateClientAsync(factory, "classified_other_admin2@example.com", UserRoles.Admin);
        var otherPolicyId = await AdminSetup.CreatePolicyAsync(factory, admin, "Uncleared policy");

        var response = await UploadAsync(user, otherPolicyId, Confidential);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNotClearedAsync(response);
    }

    [Fact]
    public async Task Upload_AtOwnLevelUnderClearedPolicy_BoundaryIsAllowed_ButOneAboveIsNot()
    {
        var (user, policyId) = await CreateClearedUserAsync("classified_boundary", Secret);

        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(user, policyId, Secret)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(user, policyId, Confidential)).StatusCode);
        var above = await UploadAsync(user, policyId, Secret + 1);
        Assert.Equal(HttpStatusCode.BadRequest, above.StatusCode);
        await AssertNotClearedAsync(above);
    }

    [Fact]
    public async Task ClassificationOptions_LevelsAreExactlyTheStandardFiveAsNameAndPriority()
    {
        var (user, _) = await CreateClearedUserAsync("classified_levels", Secret);

        using var json = JsonDocument.Parse(await user.GetStringAsync(OptionsEndpoint));

        var root = json.RootElement;
        Assert.Equal([LevelsProperty, PoliciesProperty], root.EnumerateObject().Select(p => p.Name).ToArray());
        var levels = root.GetProperty(LevelsProperty).EnumerateArray().ToList();
        Assert.All(levels, level =>
            Assert.Equal([NameProperty, PriorityProperty], level.EnumerateObject().Select(p => p.Name).ToArray()));
        var actual = levels.Select(l => (l.GetProperty(NameProperty).GetString()!, l.GetProperty(PriorityProperty).GetInt32()));
        Assert.Equal(StandardLevels, actual.ToArray());
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, Confidential)]
    [InlineData(true, NonStandardPriority)]
    public async Task Upload_PartialOrUnknownClassification_ValidatorAnswers400First(bool withPolicy, int? levelPriority)
    {
        var prefix = $"classified_validator_{withPolicy}_{levelPriority}";
        var (user, policyId) = await CreateClearedUserAsync(prefix, Secret);

        var response = await UploadAsync(user, withPolicy ? policyId : null, levelPriority);

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.BadRequest, ValidationCode, ValidationMessage);
    }

    [Fact]
    public async Task ClassificationOptions_ListLevelsAndClearedPoliciesWithHighestLevel()
    {
        var (user, policyId) = await CreateClearedUserAsync("classified_options", Secret);

        var options = await user.GetFromJsonAsync<OptionsResponse>(OptionsEndpoint);

        Assert.Equal(5, options!.Levels.Count);
        var policy = Assert.Single(options.Policies);
        Assert.Equal((policyId, Secret), (policy.PolicyId, policy.MaxPriority));
    }

    [Fact]
    public async Task ClassificationOptions_PolicyOption_HasOrganizationNameField()
    {
        var (user, _) = await CreateClearedUserAsync("classified_shape", Secret);

        using var json = JsonDocument.Parse(await user.GetStringAsync(OptionsEndpoint));

        var policy = Assert.Single(json.RootElement.GetProperty(PoliciesProperty).EnumerateArray().ToList());
        Assert.Equal(PolicyOptionProperties, policy.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("classified_shape policy organization", policy.GetProperty(OrganizationNameProperty).GetString());
    }

    // Organization name sorts first, the policy name only breaks ties
    [Fact]
    public async Task ClassificationOptions_Policies_OrderedByOrganizationNameThenPolicyName()
    {
        const string prefix = "classified_order";
        var admin = await TestUsers.CreateClientAsync(factory, $"{prefix}_admin@example.com", UserRoles.Admin);
        var user = await TestUsers.CreateClientAsync(factory, $"{prefix}_user@example.com");
        var userId = await TestUsers.GetIdAsync(factory, $"{prefix}_user@example.com");
        var (laterOrganization, laterOrgAdmin) =
            await AdminSetup.CreateOrganizationWithOrgAdminAsync(factory, admin, "Z order organization");
        var (firstOrganization, firstOrgAdmin) =
            await AdminSetup.CreateOrganizationWithOrgAdminAsync(factory, admin, "M order organization");
        var policyIds = new[]
        {
            await AdminSetup.CreatePolicyAsync(laterOrgAdmin, "A order policy", laterOrganization),
            await AdminSetup.CreatePolicyAsync(firstOrgAdmin, "B order policy", firstOrganization),
            await AdminSetup.CreatePolicyAsync(firstOrgAdmin, "A order policy", firstOrganization)
        };
        await AdminSetup.AddMemberAsync(laterOrgAdmin, laterOrganization, userId, MembershipSetup.MemberRole);
        await AdminSetup.AddMemberAsync(firstOrgAdmin, firstOrganization, userId, MembershipSetup.MemberRole);
        await AdminSetup.CreateClearanceAsync(laterOrgAdmin, laterOrganization, userId, policyIds[0], "Secret", Secret);
        await AdminSetup.CreateClearanceAsync(firstOrgAdmin, firstOrganization, userId, policyIds[1], "Secret", Secret);
        await AdminSetup.CreateClearanceAsync(firstOrgAdmin, firstOrganization, userId, policyIds[2], "Secret", Secret);

        var options = await user.GetFromJsonAsync<OptionsResponse>(OptionsEndpoint);

        Assert.Equal(
            [("M order organization", "A order policy"), ("M order organization", "B order policy"), ("Z order organization", "A order policy")],
            options!.Policies.Select(p => (p.OrganizationName, p.PolicyName)).ToArray());
    }

    [Fact]
    public async Task ClassificationOptions_Anonymous_Returns401()
    {
        var response = await factory.CreateClient().GetAsync(OptionsEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- helpers ---

    private static async Task AssertNotClearedAsync(HttpResponseMessage response)
    {
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal((NotClearedCode, NotClearedMessage), (error!.Code, error.Description));
    }

    private async Task<(HttpClient User, Guid PolicyId)> CreateClearedUserAsync(string prefix, int priority)
    {
        var admin = await TestUsers.CreateClientAsync(factory, $"{prefix}_admin@example.com", UserRoles.Admin);
        var user = await TestUsers.CreateClientAsync(factory, $"{prefix}_user@example.com");
        var userId = await TestUsers.GetIdAsync(factory, $"{prefix}_user@example.com");
        // Since #95 step 3b the organization admin of the policy's organization grants it to a member (C5, C6)
        var (organizationId, orgAdmin, policyId) = await AdminSetup.CreatePolicyWithOrgAdminAsync(factory, admin, $"{prefix} policy");
        await AdminSetup.AddMemberAndGrantClearanceAsync(orgAdmin, organizationId, userId, policyId, $"Level {priority}", priority);
        return (user, policyId);
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid? policyId, int? levelPriority)
    {
        var file = new ByteArrayContent("classified"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var form = new MultipartFormDataContent { { file, "file", "classified.txt" } };
        if (policyId is not null) form.Add(new StringContent(policyId.ToString()!), "policyId");
        if (levelPriority is not null) form.Add(new StringContent(levelPriority.ToString()!), "levelPriority");
        return client.PostAsync(UploadEndpoint, form);
    }

    private record AddDataItemResponse(Guid DataItemId);
    private record ErrorResponse(string Code, string Description);
    private record LevelResponse(string Name, int Priority);
    private record PolicyOptionResponse(Guid PolicyId, string PolicyName, string OrganizationName, int MaxPriority);
    private record OptionsResponse(List<LevelResponse> Levels, List<PolicyOptionResponse> Policies);
}

using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #23 (RenamePolicy) on SQL Server itself (a LocalDB database built by the migrations; skipped where LocalDB is
// missing): the collation is case-insensitive, so P2 must exclude the policy's own row (AC11), yet still find
// another policy's name of the organization in another letter case (AC2); the repository's UpdateAsync stores the
// new name (AC1)
public class RenamePolicySqlServerTests(LocalDbApiFactory factory) : IClassFixture<LocalDbApiFactory>
{
    private const string OldName = "Project Apollo";

    [LocalDbFact]
    public async Task RenamePolicy_NewName_StoresIt()
    {
        var (scenario, policyId) = await ArrangeAsync("sql_policy_rename");
        var newName = $"Renamed {Guid.NewGuid():N}";

        var response = await RenameAsync(scenario, policyId, newName);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(newName, await NameAsync(scenario, policyId));
    }

    [LocalDbFact]
    public async Task RenamePolicy_NewLetterCaseOfItsOwnName_Returns204AndStoresIt()
    {
        var (scenario, policyId) = await ArrangeAsync("sql_policy_case");
        const string upperCase = "PROJECT APOLLO";

        var response = await RenameAsync(scenario, policyId, upperCase);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(upperCase, await NameAsync(scenario, policyId));
    }

    [LocalDbFact]
    public async Task RenamePolicy_TrailingSpaceOfItsOwnName_Returns204AndStoresIt()
    {
        var (scenario, policyId) = await ArrangeAsync("sql_policy_space");
        const string withTrailingSpace = "Project Apollo ";

        var response = await RenameAsync(scenario, policyId, withTrailingSpace);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(withTrailingSpace, await NameAsync(scenario, policyId));
    }

    [LocalDbFact]
    public async Task RenamePolicy_OtherLetterCaseOfAnotherPolicysName_Returns409NameAlreadyInUse()
    {
        var (scenario, policyId) = await ArrangeAsync("sql_policy_taken");
        await CreatePolicyAsync(scenario.OrgAdmin, "Project Gemini", scenario.OrganizationId);

        var response = await RenameAsync(scenario, policyId, "PROJECT GEMINI");

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.Conflict,
            AdminErrorCodes.PolicyNameAlreadyInUse, AdminErrorMessages.PolicyNameAlreadyInUse);
    }

    // --- helpers ---

    private async Task<(OrganizationScenario Scenario, Guid PolicyId)> ArrangeAsync(string prefix)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"{prefix}_{Guid.NewGuid():N}");
        return (scenario, await CreatePolicyAsync(scenario.OrgAdmin, OldName, scenario.OrganizationId));
    }

    private static Task<HttpResponseMessage> RenameAsync(OrganizationScenario scenario, Guid policyId, string name) =>
        scenario.OrgAdmin.PutAsJsonAsync(PolicyEndpoint(scenario.OrganizationId, policyId), new { Name = name });

    private static async Task<string> NameAsync(OrganizationScenario scenario, Guid policyId) =>
        (await scenario.OrgAdmin.GetFromJsonAsync<PolicyResponse>(PolicyEndpoint(scenario.OrganizationId, policyId)))!.Name;

    private record PolicyResponse(Guid Id, string Name, Guid OrganizationId);
}

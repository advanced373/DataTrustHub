using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// #23 (RenamePolicy): an organization admin renames a policy of their organization (C6, ADR-0014); the name stays
// unique within the organization (P2). Tests in a class run one after another, so "the audit records added by
// this request" is exact
public class RenamePolicyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int MaxNameLength = 256;
    private const string RenamePolicyAction = "RenamePolicy";
    private const string SucceededOutcome = "Succeeded";
    private const string RejectedOutcome = "Rejected";
    private const string OldName = "Project Apollo";
    private const string OtherName = "Project Gemini";
    private const string UploadEndpoint = "/data/upload";
    private const string ReaderLevelName = "Secret";
    private const int ReaderLevelPriority = 3;
    private const int ItemPriority = 2;

    public static TheoryData<string> NotOrgAdminCallers => new(OrganizationCallers.NotOrgAdminKinds);

    // --- AC1: an organization admin renames a policy of their organization ---

    [Fact]
    public async Task RenamePolicy_OrgAdmin_Returns204()
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_ok");

        var response = await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, policyId, NewName("Renamed"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RenamePolicy_OrgAdmin_GetPolicyReturnsTheNewName()
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_get");
        var newName = NewName("Renamed get");

        await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, policyId, newName);

        Assert.Equal(newName, (await GetPolicyAsync(scenario, policyId)).Name);
    }

    // --- AC2: the name of another policy of the same organization (P2) ---

    [Fact]
    public async Task RenamePolicy_NameOfAnotherPolicyOfTheOrganization_Returns409NameAlreadyInUse()
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_taken");
        await CreatePolicyAsync(scenario.OrgAdmin, OtherName, scenario.OrganizationId);

        var response = await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, policyId, OtherName);

        await AssertNameAlreadyInUseAsync(response);
    }

    [Fact]
    public async Task RenamePolicy_NameOfAnotherPolicyOfTheOrganization_KeepsTheOldName()
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_taken_keep");
        await CreatePolicyAsync(scenario.OrgAdmin, OtherName, scenario.OrganizationId);

        await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, policyId, OtherName);

        Assert.Equal(OldName, (await GetPolicyAsync(scenario, policyId)).Name);
    }

    // --- AC3: P2 is per organization ---

    [Fact]
    public async Task RenamePolicy_NameOfAPolicyOfAnotherOrganization_Returns204AndStoresIt()
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_other_org_name");
        var other = await CreateOrganizationWithOrgAdminAsync(factory, Prefix("rename_policy_other_org_name_other"));
        await CreatePolicyAsync(other.OrgAdmin, OtherName, other.OrganizationId);

        var response = await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, policyId, OtherName);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(OtherName, (await GetPolicyAsync(scenario, policyId)).Name);
    }

    // --- AC4: exactly its current name is no conflict with itself ---

    [Fact]
    public async Task RenamePolicy_ToItsCurrentName_Returns204AndKeepsThePolicy()
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_same");

        var response = await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, policyId, OldName);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new PolicyResponse(policyId, OldName, scenario.OrganizationId), await GetPolicyAsync(scenario, policyId));
    }

    // --- AC5: a caller who is not an organization admin of the organization ---

    [Theory]
    [MemberData(nameof(NotOrgAdminCallers))]
    public async Task RenamePolicy_CallerNotOrgAdmin_Returns404OrganizationNotFound(string callerKind)
    {
        var prefix = Prefix($"rename_policy_caller_{callerKind}");
        var (scenario, policyId) = await ArrangeAsync(prefix);
        var caller = await OrganizationCallers.CreateNotOrgAdminAsync(factory, scenario, callerKind, prefix);

        var response = await RenameAsync(caller, scenario.OrganizationId, policyId, NewName("Not org admin"));

        await AssertOrganizationNotFoundAsync(response);
    }

    [Theory]
    [MemberData(nameof(NotOrgAdminCallers))]
    public async Task RenamePolicy_CallerNotOrgAdmin_KeepsTheName(string callerKind)
    {
        var prefix = Prefix($"rename_policy_caller_kept_{callerKind}");
        var (scenario, policyId) = await ArrangeAsync(prefix);
        var caller = await OrganizationCallers.CreateNotOrgAdminAsync(factory, scenario, callerKind, prefix);

        await RenameAsync(caller, scenario.OrganizationId, policyId, NewName("Not org admin kept"));

        Assert.Equal(OldName, (await GetPolicyAsync(scenario, policyId)).Name);
    }

    // The policy's name is taken in the organization: the caller check must win, so P2 reveals nothing
    [Fact]
    public async Task RenamePolicy_CallerNotOrgAdminAndNameTaken_Returns404OrganizationNotFound()
    {
        var prefix = Prefix("rename_policy_caller_taken");
        var (scenario, policyId) = await ArrangeAsync(prefix);
        await CreatePolicyAsync(scenario.OrgAdmin, OtherName, scenario.OrganizationId);
        var caller = await OrganizationCallers.CreateNotOrgAdminAsync(factory, scenario, OrganizationCallers.Member, prefix);

        var response = await RenameAsync(caller, scenario.OrganizationId, policyId, OtherName);

        await AssertOrganizationNotFoundAsync(response);
    }

    [Fact]
    public async Task RenamePolicy_MissingOrganization_Returns404OrganizationNotFound()
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_missing_org");

        var response = await RenameAsync(scenario.OrgAdmin, Guid.NewGuid(), policyId, NewName("Missing organization"));

        await AssertOrganizationNotFoundAsync(response);
    }

    [Fact]
    public async Task RenamePolicy_PlatformAdminAndMissingOrganization_Returns404OrganizationNotFound()
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_missing_org_platform");

        var response = await RenameAsync(scenario.PlatformAdmin, Guid.NewGuid(), policyId, NewName("Missing platform"));

        await AssertOrganizationNotFoundAsync(response);
    }

    // --- AC6: an unknown policy, or a policy of another organization ---

    [Fact]
    public async Task RenamePolicy_UnknownPolicy_Returns404PolicyNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, Prefix("rename_policy_unknown"));

        var response = await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, Guid.NewGuid(), NewName("Unknown"));

        await AssertPolicyNotFoundAsync(response);
    }

    [Fact]
    public async Task RenamePolicy_PolicyOfAnotherOrganizationThroughOwnRoute_Returns404PolicyNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, Prefix("rename_policy_foreign"));
        var (_, otherPolicyId) = await ArrangeAsync("rename_policy_foreign_other");

        var response = await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, otherPolicyId, NewName("Foreign"));

        await AssertPolicyNotFoundAsync(response);
    }

    [Fact]
    public async Task RenamePolicy_PolicyOfAnotherOrganizationThroughOwnRoute_KeepsItsName()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, Prefix("rename_policy_foreign_kept"));
        var (other, otherPolicyId) = await ArrangeAsync("rename_policy_foreign_kept_other");

        await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, otherPolicyId, NewName("Foreign kept"));

        Assert.Equal(new PolicyResponse(otherPolicyId, OldName, other.OrganizationId), await GetPolicyAsync(other, otherPolicyId));
    }

    // A 409 from P2 in the other organization would reveal its policy, so the scope check must come first
    [Fact]
    public async Task RenamePolicy_PolicyOfAnotherOrganizationToItsTakenName_Returns404PolicyNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, Prefix("rename_policy_foreign_taken"));
        var (other, otherPolicyId) = await ArrangeAsync("rename_policy_foreign_taken_other");
        await CreatePolicyAsync(other.OrgAdmin, OtherName, other.OrganizationId);

        var response = await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, otherPolicyId, OtherName);

        await AssertPolicyNotFoundAsync(response);
    }

    // --- AC7: an empty name, or one longer than 256 characters; no token ---

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task RenamePolicy_EmptyName_Returns400(string? name)
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_empty");

        var response = await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, policyId, name);

        await AssertValidationErrorAsync(response);
    }

    [Fact]
    public async Task RenamePolicy_NameLongerThan256_Returns400AndKeepsTheName()
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_long");

        var response = await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, policyId, new string('a', MaxNameLength + 1));

        await AssertValidationErrorAsync(response);
        Assert.Equal(OldName, (await GetPolicyAsync(scenario, policyId)).Name);
    }

    [Fact]
    public async Task RenamePolicy_NameOf256_Returns204AndStoresIt()
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_max");
        var newName = new string('a', MaxNameLength);

        var response = await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, policyId, newName);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(newName, (await GetPolicyAsync(scenario, policyId)).Name);
    }

    [Fact]
    public async Task RenamePolicy_EmptyPolicyId_Returns400()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, Prefix("rename_policy_empty_id"));

        var response = await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, Guid.Empty, NewName("Empty id"));

        await AssertValidationErrorAsync(response);
    }

    [Fact]
    public async Task RenamePolicy_Anonymous_Returns401AndKeepsTheName()
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_anonymous");

        var response = await RenameAsync(factory.CreateClient(), scenario.OrganizationId, policyId, NewName("Anonymous"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(OldName, (await GetPolicyAsync(scenario, policyId)).Name);
    }

    // --- AC9: audited with the policy as its target, scoped to the organization ---

    [Fact]
    public async Task RenamePolicy_Succeeded_RecordsTheActorAndThePolicyAsTarget()
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_audit");

        var records = await RecordsAddedByAsync(() =>
            RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, policyId, NewName("Audited")));

        var record = Assert.Single(records);
        Assert.Equal(
            (RenamePolicyAction, (Guid?)scenario.OrgAdminId, SucceededOutcome, (string?)null),
            (record.Action, record.ActorUserId, record.Outcome, record.ErrorCode));
        Assert.Equal(((string?)AuditTargetTypes.Policy, (Guid?)policyId), (record.TargetType, record.TargetId));
    }

    [Fact]
    public async Task RenamePolicy_Succeeded_RecordIsScopedToTheOrganization()
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_audit_scope");

        var records = await RecordsAddedByAsync(() =>
            RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, policyId, NewName("Audited scope")));

        Assert.Equal(scenario.OrganizationId, Assert.Single(records).OrganizationId);
    }

    [Fact]
    public async Task RenamePolicy_NameOfAnotherPolicy_RecordsTheRejectionWithTheTarget()
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_audit_taken");
        await CreatePolicyAsync(scenario.OrgAdmin, OtherName, scenario.OrganizationId);

        var records = await RecordsAddedByAsync(() =>
            RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, policyId, OtherName));

        var record = Assert.Single(records);
        Assert.Equal(
            (RenamePolicyAction, RejectedOutcome, (string?)AdminErrorCodes.PolicyNameAlreadyInUse, (Guid?)scenario.OrganizationId),
            (record.Action, record.Outcome, record.ErrorCode, record.OrganizationId));
        Assert.Equal(((string?)AuditTargetTypes.Policy, (Guid?)policyId), (record.TargetType, record.TargetId));
    }

    // --- AC10: the policy keeps its id, organization and clearances; its data items stay readable ---

    [Fact]
    public async Task RenamePolicy_KeepsTheIdAndTheOrganization()
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_keeps");
        var newName = NewName("Kept");

        (await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, policyId, newName)).EnsureSuccessStatusCode();

        var stored = Assert.Single(await StoredPoliciesAsync(factory), p => p.Id == policyId);
        Assert.Equal((newName, scenario.OrganizationId), (stored.Name, stored.OrganizationId));
    }

    [Fact]
    public async Task RenamePolicy_DoesNotAddOrRemoveAPolicy()
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_count");
        var before = (await StoredPoliciesAsync(factory)).Select(p => p.Id).ToHashSet();

        (await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, policyId, NewName("Count"))).EnsureSuccessStatusCode();

        Assert.Equal(before, (await StoredPoliciesAsync(factory)).Select(p => p.Id).ToHashSet());
    }

    [Fact]
    public async Task RenamePolicy_KeepsItsClearances()
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_clearances");
        var (_, holderId) = await CreateUserAsync(factory, Prefix("rename_policy_clearances_holder"));
        var clearanceId = await AddMemberAndGrantClearanceAsync(
            scenario.OrgAdmin, scenario.OrganizationId, holderId, policyId, ReaderLevelName, ReaderLevelPriority);
        var before = ClearancesOn(await StoredClearancesAsync(factory), policyId);

        (await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, policyId, NewName("Clearances"))).EnsureSuccessStatusCode();

        Assert.Equal(before, ClearancesOn(await StoredClearancesAsync(factory), policyId));
        Assert.Contains((clearanceId, holderId), before);
    }

    [Fact]
    public async Task RenamePolicy_ClassifiedItemStaysReadableByAClearedReader()
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_read");
        var itemId = await UploadClassifiedItemAsync(scenario, policyId, "rename_policy_read_owner");
        var reader = await CreateClearedUserAsync(scenario, policyId, "rename_policy_read_reader");
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"/data/{itemId}")).StatusCode);

        (await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, policyId, NewName("Read"))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"/data/{itemId}")).StatusCode);
    }

    [Fact]
    public async Task RenamePolicy_ClassifiedItemStaysHiddenFromAnUnclearedUser()
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_hidden");
        var itemId = await UploadClassifiedItemAsync(scenario, policyId, "rename_policy_hidden_owner");
        var (uncleared, _) = await CreateUserAsync(factory, Prefix("rename_policy_hidden_uncleared"));

        (await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, policyId, NewName("Hidden"))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await uncleared.GetAsync($"/data/{itemId}")).StatusCode);
    }

    // --- AC11: a new letter case or spacing of its own name is no conflict with itself ---

    [Theory]
    [InlineData("PROJECT APOLLO")]
    [InlineData("project apollo")]
    [InlineData(" Project Apollo ")]
    [InlineData("Project Apollo ")]
    public async Task RenamePolicy_NewSpellingOfItsOwnName_Returns204AndStoresIt(string newSpelling)
    {
        var (scenario, policyId) = await ArrangeAsync("rename_policy_spelling");

        var response = await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, policyId, newSpelling);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(newSpelling, (await GetPolicyAsync(scenario, policyId)).Name);
    }

    // --- helpers ---

    private static string Prefix(string name) => $"{name}_{Guid.NewGuid():N}";

    private static string NewName(string name) => $"{name} {Guid.NewGuid():N}";

    // An organization with its organization admin and one policy named OldName
    private async Task<(OrganizationScenario Scenario, Guid PolicyId)> ArrangeAsync(string prefix)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, Prefix(prefix));
        return (scenario, await CreatePolicyAsync(scenario.OrgAdmin, OldName, scenario.OrganizationId));
    }

    private static Task<HttpResponseMessage> RenameAsync(HttpClient caller, Guid organizationId, Guid policyId, string? name) =>
        caller.PutAsJsonAsync(PolicyEndpoint(organizationId, policyId), new { Name = name });

    private static async Task<PolicyResponse> GetPolicyAsync(OrganizationScenario scenario, Guid policyId) =>
        (await scenario.OrgAdmin.GetFromJsonAsync<PolicyResponse>(PolicyEndpoint(scenario.OrganizationId, policyId)))!;

    private static HashSet<(Guid Id, Guid UserId)> ClearancesOn(List<DbClearance> clearances, Guid policyId) =>
        clearances.Where(c => c.PolicyId == policyId).Select(c => (c.Id, c.UserId)).ToHashSet();

    // The organization admin adds the user as a member and grants them a clearance on the policy (C5, C6)
    private async Task<HttpClient> CreateClearedUserAsync(OrganizationScenario scenario, Guid policyId, string name)
    {
        var (client, userId) = await CreateUserAsync(factory, Prefix(name));
        await AddMemberAndGrantClearanceAsync(
            scenario.OrgAdmin, scenario.OrganizationId, userId, policyId, ReaderLevelName, ReaderLevelPriority);
        return client;
    }

    // A cleared owner uploads one item classified under the policy
    private async Task<Guid> UploadClassifiedItemAsync(OrganizationScenario scenario, Guid policyId, string ownerName)
    {
        var owner = await CreateClearedUserAsync(scenario, policyId, ownerName);
        var file = new ByteArrayContent("classified"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var form = new MultipartFormDataContent
        {
            { file, "file", "classified.txt" },
            { new StringContent(policyId.ToString()), "policyId" },
            { new StringContent(ItemPriority.ToString()), "levelPriority" }
        };
        var response = await owner.PostAsync(UploadEndpoint, form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AddDataItemResponse>())!.DataItemId;
    }

    private static Task AssertNameAlreadyInUseAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(response, HttpStatusCode.Conflict,
            AdminErrorCodes.PolicyNameAlreadyInUse, AdminErrorMessages.PolicyNameAlreadyInUse);

    private static Task AssertOrganizationNotFoundAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(response, HttpStatusCode.NotFound,
            AdminErrorCodes.OrganizationNotFound, AdminErrorMessages.OrganizationNotFound);

    private static Task AssertPolicyNotFoundAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(response, HttpStatusCode.NotFound, AdminErrorCodes.PolicyNotFound, AdminErrorMessages.PolicyNotFound);

    private static Task AssertValidationErrorAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(response, HttpStatusCode.BadRequest,
            AdminErrorCodes.ValidationGeneral, AdminErrorMessages.ValidationGeneral);

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

    private record PolicyResponse(Guid Id, string Name, Guid OrganizationId);
    private record AddDataItemResponse(Guid DataItemId);
}

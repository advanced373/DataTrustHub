using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// #23: the platform admin renames an organization (ADR-0014); the name stays unique across organizations (O1).
// Tests in a class run one after another, so "the audit records added by this request" is exact
public class RenameOrganizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int MaxNameLength = 256;
    private const string RenameOrganizationAction = "RenameOrganization";
    private const string SucceededOutcome = "Succeeded";
    private const string RejectedOutcome = "Rejected";
    private const string PolicyName = "Kept policy";

    // --- AC1: the platform admin renames an organization ---

    [Fact]
    public async Task RenameOrganization_PlatformAdmin_Returns204()
    {
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, Prefix("rename_ok"));

        var response = await RenameAsync(platformAdmin, organizationId, NewName("Renamed"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RenameOrganization_PlatformAdmin_GetOrganizationReturnsTheNewName()
    {
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, Prefix("rename_get"));
        var newName = NewName("Renamed get");

        await RenameAsync(platformAdmin, organizationId, newName);

        Assert.Equal(newName, (await GetOrganizationAsync(platformAdmin, organizationId)).Name);
    }

    // --- AC2: a name another organization has (O1) ---

    [Fact]
    public async Task RenameOrganization_NameOfAnotherOrganization_Returns409NameAlreadyInUse()
    {
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, Prefix("rename_taken"));
        var takenName = NewName("Taken");
        await CreateOrganizationAsync(platformAdmin, takenName);

        var response = await RenameAsync(platformAdmin, organizationId, takenName);

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.Conflict,
            AdminErrorCodes.OrganizationNameAlreadyInUse, AdminErrorMessages.OrganizationNameAlreadyInUse);
    }

    [Fact]
    public async Task RenameOrganization_NameOfAnotherOrganization_KeepsTheOldName()
    {
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, Prefix("rename_taken_keep"));
        var oldName = (await GetOrganizationAsync(platformAdmin, organizationId)).Name;
        var takenName = NewName("Taken keep");
        await CreateOrganizationAsync(platformAdmin, takenName);

        await RenameAsync(platformAdmin, organizationId, takenName);

        Assert.Equal(oldName, (await GetOrganizationAsync(platformAdmin, organizationId)).Name);
    }

    // --- AC3: exactly its current name is no conflict with itself ---

    [Fact]
    public async Task RenameOrganization_ToItsCurrentName_Returns204AndKeepsTheName()
    {
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, Prefix("rename_same"));
        var currentName = (await GetOrganizationAsync(platformAdmin, organizationId)).Name;

        var response = await RenameAsync(platformAdmin, organizationId, currentName);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(currentName, (await GetOrganizationAsync(platformAdmin, organizationId)).Name);
    }

    // --- AC4: an unknown organization ---

    [Fact]
    public async Task RenameOrganization_UnknownOrganization_Returns404NotFound()
    {
        var platformAdmin = await CreatePlatformAdminAsync("rename_missing");

        var response = await RenameAsync(platformAdmin, Guid.NewGuid(), NewName("Missing"));

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.NotFound,
            AdminErrorCodes.OrganizationNotFound, AdminErrorMessages.OrganizationNotFound);
    }

    // --- AC5: an empty name, or one longer than 256 characters ---

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task RenameOrganization_EmptyName_Returns400(string? name)
    {
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, Prefix("rename_empty"));

        var response = await RenameAsync(platformAdmin, organizationId, name);

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.BadRequest,
            AdminErrorCodes.ValidationGeneral, AdminErrorMessages.ValidationGeneral);
    }

    [Fact]
    public async Task RenameOrganization_NameLongerThan256_Returns400AndKeepsTheName()
    {
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, Prefix("rename_long"));
        var oldName = (await GetOrganizationAsync(platformAdmin, organizationId)).Name;

        var response = await RenameAsync(platformAdmin, organizationId, new string('a', MaxNameLength + 1));

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.BadRequest,
            AdminErrorCodes.ValidationGeneral, AdminErrorMessages.ValidationGeneral);
        Assert.Equal(oldName, (await GetOrganizationAsync(platformAdmin, organizationId)).Name);
    }

    [Fact]
    public async Task RenameOrganization_NameOf256_Returns204()
    {
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, Prefix("rename_max"));
        var newName = Guid.NewGuid().ToString("N").PadRight(MaxNameLength, 'a');

        var response = await RenameAsync(platformAdmin, organizationId, newName);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(newName, (await GetOrganizationAsync(platformAdmin, organizationId)).Name);
    }

    // --- AC6: only the platform admin ---

    [Fact]
    public async Task RenameOrganization_Anonymous_Returns401()
    {
        var (_, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, Prefix("rename_anonymous"));

        var response = await RenameAsync(factory.CreateClient(), organizationId, NewName("Anonymous"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RenameOrganization_OrganizationAdmin_Returns403AndKeepsTheName()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, Prefix("rename_orgadmin"));
        var oldName = (await GetOrganizationAsync(scenario.PlatformAdmin, scenario.OrganizationId)).Name;

        var response = await RenameAsync(scenario.OrgAdmin, scenario.OrganizationId, NewName("Org admin"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(oldName, (await GetOrganizationAsync(scenario.PlatformAdmin, scenario.OrganizationId)).Name);
    }

    [Fact]
    public async Task RenameOrganization_RegularUser_Returns403()
    {
        var (_, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, Prefix("rename_regular"));
        var (user, _) = await CreateUserAsync(factory, Prefix("rename_regular_user"));

        var response = await RenameAsync(user, organizationId, NewName("Regular"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- AC8: audited with the organization as its target ---

    [Fact]
    public async Task RenameOrganization_Succeeded_RecordsTheActorAndTheOrganizationAsTarget()
    {
        var prefix = Prefix("rename_audit");
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, prefix);
        var platformAdminId = await PlatformAdminIdAsync(factory, prefix);

        var records = await RecordsAddedByAsync(() => RenameAsync(platformAdmin, organizationId, NewName("Audited")));

        var record = Assert.Single(records);
        Assert.Equal(
            (RenameOrganizationAction, (Guid?)platformAdminId, SucceededOutcome, (string?)null),
            (record.Action, record.ActorUserId, record.Outcome, record.ErrorCode));
        Assert.Equal(((string?)AuditTargetTypes.Organization, (Guid?)organizationId), (record.TargetType, record.TargetId));
    }

    [Fact]
    public async Task RenameOrganization_NameOfAnotherOrganization_RecordsTheRejectionWithTheTarget()
    {
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, Prefix("rename_audit_taken"));
        var takenName = NewName("Audit taken");
        await CreateOrganizationAsync(platformAdmin, takenName);

        var records = await RecordsAddedByAsync(() => RenameAsync(platformAdmin, organizationId, takenName));

        var record = Assert.Single(records);
        Assert.Equal(
            (RenameOrganizationAction, RejectedOutcome, (string?)AdminErrorCodes.OrganizationNameAlreadyInUse),
            (record.Action, record.Outcome, record.ErrorCode));
        Assert.Equal(((string?)AuditTargetTypes.Organization, (Guid?)organizationId), (record.TargetType, record.TargetId));
    }

    // --- AC9: the organization keeps its id, memberships and policies ---

    [Fact]
    public async Task RenameOrganization_KeepsTheIdMembershipsAndPolicies()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, Prefix("rename_keeps"));
        var policyId = await CreatePolicyAsync(scenario.OrgAdmin, PolicyName, scenario.OrganizationId);
        var membershipsBefore = (await StoredMembershipsAsync(factory, scenario.OrganizationId)).Select(m => m.Id).ToHashSet();
        var newName = NewName("Kept");

        (await RenameAsync(scenario.PlatformAdmin, scenario.OrganizationId, newName)).EnsureSuccessStatusCode();

        Assert.Equal(new OrganizationResponse(scenario.OrganizationId, newName),
            await GetOrganizationAsync(scenario.PlatformAdmin, scenario.OrganizationId));
        Assert.Equal(membershipsBefore, (await StoredMembershipsAsync(factory, scenario.OrganizationId)).Select(m => m.Id).ToHashSet());
        Assert.Contains(await StoredPoliciesAsync(factory), p => p.Id == policyId && p.OrganizationId == scenario.OrganizationId);
    }

    [Fact]
    public async Task RenameOrganization_DoesNotAddOrRemoveAnOrganization()
    {
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, Prefix("rename_count"));
        var before = await StoredOrganizationIdsAsync();

        (await RenameAsync(platformAdmin, organizationId, NewName("Count"))).EnsureSuccessStatusCode();

        Assert.Equal(before, await StoredOrganizationIdsAsync());
    }

    // --- AC10: a new letter case or spacing of its own name is no conflict with itself ---

    [Theory]
    [InlineData("ACME {0}")]
    [InlineData("acme {0}")]
    [InlineData(" Acme {0} ")]
    public async Task RenameOrganization_NewSpellingOfItsOwnName_Returns204AndStoresIt(string spellingFormat)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var platformAdmin = await CreatePlatformAdminAsync($"rename_spelling_{suffix}");
        var organizationId = await CreateOrganizationAsync(platformAdmin, $"Acme {suffix}");
        var newSpelling = string.Format(spellingFormat, suffix);

        var response = await RenameAsync(platformAdmin, organizationId, newSpelling);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(newSpelling, (await GetOrganizationAsync(platformAdmin, organizationId)).Name);
    }

    // --- helpers ---

    private static string Prefix(string name) => $"{name}_{Guid.NewGuid():N}";

    private static string NewName(string name) => $"{name} {Guid.NewGuid():N}";

    private Task<HttpClient> CreatePlatformAdminAsync(string name) =>
        TestUsers.CreateClientAsync(factory, $"{name}_{Guid.NewGuid():N}@example.com", UserRoles.Admin);

    private static Task<HttpResponseMessage> RenameAsync(HttpClient caller, Guid organizationId, string? name) =>
        caller.PutAsJsonAsync($"{OrganizationsEndpoint}/{organizationId}", new { Name = name });

    private static async Task<OrganizationResponse> GetOrganizationAsync(HttpClient platformAdmin, Guid organizationId) =>
        (await platformAdmin.GetFromJsonAsync<OrganizationResponse>($"{OrganizationsEndpoint}/{organizationId}"))!;

    private async Task<HashSet<Guid>> StoredOrganizationIdsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        return (await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync()).ToHashSet();
    }

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

    private record OrganizationResponse(Guid Id, string Name);
}

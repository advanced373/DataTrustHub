using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DataTrustHub.Features.Tests.Administration.Helpers;

// An organization with a platform admin who created it and an organization admin appointed by them
internal sealed record OrganizationScenario(HttpClient PlatformAdmin, Guid OrganizationId, HttpClient OrgAdmin, Guid OrgAdminId);

// For C7: a user's clearance on a policy of the scenario's organization, their clearance on a policy of another
// organization, and another user's clearance on the scenario's policy
internal sealed record ClearanceScenario(Guid UserClearanceHere, Guid UserClearanceElsewhere, Guid OtherUserId, Guid OtherUserClearanceHere);

/// <summary>
/// Creates organizations, users and memberships through the API (ADR-0014), and reads the stored memberships.
/// </summary>
internal static class MembershipSetup
{
    internal const string MemberRole = "Member";
    internal const string GuestRole = "Guest";
    internal const string OrgAdminRole = "OrgAdmin";

    private const string EmailDomain = "@example.com";

    internal static async Task<OrganizationScenario> CreateOrganizationWithOrgAdminAsync(ApiFactory factory, string prefix)
    {
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, prefix);
        var (orgAdmin, orgAdminId) = await CreateUserAsync(factory, $"{prefix}_orgadmin");
        await AdminSetup.AppointOrganizationAdminAsync(platformAdmin, organizationId, orgAdminId);
        return new OrganizationScenario(platformAdmin, organizationId, orgAdmin, orgAdminId);
    }

    // A platform admin and an organization they created, which has no memberships yet
    internal static async Task<(HttpClient PlatformAdmin, Guid OrganizationId)> CreateOrganizationWithoutMembersAsync(
        ApiFactory factory, string prefix)
    {
        var platformAdmin = await TestUsers.CreateClientAsync(factory, PlatformAdminEmail(prefix), UserRoles.Admin);
        return (platformAdmin, await AdminSetup.CreateOrganizationAsync(platformAdmin, $"{prefix} organization"));
    }

    internal static Task<Guid> PlatformAdminIdAsync(ApiFactory factory, string prefix) =>
        TestUsers.GetIdAsync(factory, PlatformAdminEmail(prefix));

    // A regular user (UserRoles.User) and a client authenticated with their JWT
    internal static async Task<(HttpClient Client, Guid UserId)> CreateUserAsync(ApiFactory factory, string name)
    {
        var email = $"{name}{EmailDomain}";
        var client = await TestUsers.CreateClientAsync(factory, email);
        return (client, await TestUsers.GetIdAsync(factory, email));
    }

    // Stores a membership directly, without the use cases, e.g. a member in an organization without an organization admin
    internal static async Task<Guid> SeedMembershipAsync(ApiFactory factory, Membership membership)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMembershipRepository>().Add(membership);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return membership.Id;
    }

    // Removes a membership row directly, without the use cases (and so without M2 and C7)
    internal static async Task RemoveMembershipAsync(ApiFactory factory, Guid membershipId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        db.Memberships.Remove(await db.Memberships.SingleAsync(m => m.Id == membershipId));
        await db.SaveChangesAsync();
    }

    internal static async Task<List<DbMembership>> StoredMembershipsAsync(ApiFactory factory, Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        return await db.Memberships.AsNoTracking().Where(m => m.OrganizationId == organizationId).ToListAsync();
    }

    internal static async Task<List<DbMembership>> StoredMembershipsOfUserAsync(ApiFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        return await db.Memberships.AsNoTracking().Where(m => m.UserId == userId).ToListAsync();
    }

    internal static async Task<List<Guid>> StoredClearanceIdsOfUserAsync(ApiFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        return await db.Clearances.AsNoTracking().Where(c => c.UserId == userId).Select(c => c.Id).ToListAsync();
    }

    internal static async Task<ClearanceScenario> CreateClearancesAsync(
        ApiFactory factory, OrganizationScenario scenario, Guid userId, string prefix)
    {
        var admin = scenario.PlatformAdmin;
        var policyHere = await AdminSetup.CreatePolicyAsync(scenario.OrgAdmin, $"{prefix} policy", scenario.OrganizationId);
        var policyElsewhere = await AdminSetup.CreatePolicyAsync(factory, admin, $"{prefix} elsewhere policy");
        var (_, otherUserId) = await CreateUserAsync(factory, $"{prefix}_other_cleared");
        return new ClearanceScenario(
            await AdminSetup.CreateClearanceAsync(admin, userId, policyHere),
            await AdminSetup.CreateClearanceAsync(admin, userId, policyElsewhere),
            otherUserId,
            await AdminSetup.CreateClearanceAsync(admin, otherUserId, policyHere));
    }

    private static string PlatformAdminEmail(string prefix) => $"{prefix}_platform{EmailDomain}";
}

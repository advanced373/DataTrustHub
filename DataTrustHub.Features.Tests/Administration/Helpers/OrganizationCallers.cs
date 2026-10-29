using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features.Tests.Helpers;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration.Helpers;

/// <summary>
/// Callers who are not an organization admin of a scenario's organization (ADR-0014): each one must get
/// 404 Organization.NotFound from the organization admin use cases, before anything about the target is checked.
/// </summary>
internal static class OrganizationCallers
{
    internal const string WithoutMembership = "WithoutMembership";
    internal const string Member = "Member";
    internal const string Guest = "Guest";
    internal const string OrgAdminElsewhere = "OrgAdminElsewhere";
    internal const string PlatformAdmin = "PlatformAdmin";

    internal static readonly string[] NotOrgAdminKinds = [WithoutMembership, Member, Guest, OrgAdminElsewhere, PlatformAdmin];

    internal static async Task<HttpClient> CreateNotOrgAdminAsync(
        ApiFactory factory, OrganizationScenario scenario, string kind, string prefix)
    {
        switch (kind)
        {
            case PlatformAdmin:
                // The platform admin who created the organization has no membership in it
                return scenario.PlatformAdmin;
            case OrgAdminElsewhere:
                return (await CreateOrganizationWithOrgAdminAsync(factory, $"{prefix}_elsewhere")).OrgAdmin;
        }

        var (client, userId) = await CreateUserAsync(factory, $"{prefix}_caller");
        if (kind == Member) await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, MemberRole);
        if (kind == Guest) await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, GuestRole);
        return client;
    }

    // A second organization admin, seeded directly so the setup does not depend on the use cases under test
    internal static async Task<(HttpClient Client, Guid UserId)> AddSecondOrgAdminAsync(
        ApiFactory factory, OrganizationScenario scenario, string name)
    {
        var (client, userId) = await CreateUserAsync(factory, name);
        await SeedMembershipAsync(factory, Membership.AppointOrgAdmin(userId, scenario.OrganizationId));
        return (client, userId);
    }
}

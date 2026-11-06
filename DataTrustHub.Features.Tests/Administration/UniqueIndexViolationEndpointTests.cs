using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// #121 AC1-AC4 over HTTP: the request passes the use case's check, a concurrent request saved the same values
// first, and the unique index rejects the save. The endpoint answers 409 with the use case's existing error
// code and message, as when the check finds the value taken, and stores nothing.
public class UniqueIndexViolationEndpointTests(ConcurrentSaveApiFactory factory) : IClassFixture<ConcurrentSaveApiFactory>
{
    private const string CreateAccountEndpoint = "/admin/accounts";
    private const string Password = "Password1!";
    private const string EmailDomain = "@example.com";

    // --- AC1 (U1) ---
    // Register no longer saves a user (#172): its unique-index handling (R1, 409 Registration.AlreadyPending) is
    // covered by RegisterUserUniqueIndexTests and RegistrationRequestsSqlServerTests

    [Fact]
    public async Task CreateAccount_UniqueIndexRejectsSave_Returns409AccountEmailAlreadyInUse()
    {
        var admin = await TestUsers.CreateClientAsync(factory, $"unique_account_admin_{Guid.NewGuid():N}{EmailDomain}", UserRoles.Admin);
        var email = $"unique_account_{Guid.NewGuid():N}{EmailDomain}";
        factory.ConcurrentSave.SavedFirstByAnotherRequest();

        var response = await admin.PostAsJsonAsync(CreateAccountEndpoint, new { Email = email, Password });

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.Conflict,
            AdminErrorCodes.AccountEmailAlreadyInUse, AdminErrorMessages.AccountEmailAlreadyInUse);
        Assert.False(await UserStoredAsync(email));
    }

    // --- AC2 (O1) ---

    [Fact]
    public async Task CreateOrganization_UniqueIndexRejectsSave_Returns409OrganizationNameAlreadyInUse()
    {
        var admin = await TestUsers.CreateClientAsync(factory, $"unique_org_admin_{Guid.NewGuid():N}{EmailDomain}", UserRoles.Admin);
        var name = $"Unique organization {Guid.NewGuid():N}";
        factory.ConcurrentSave.SavedFirstByAnotherRequest();

        var response = await admin.PostAsJsonAsync(OrganizationsEndpoint, new { Name = name });

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.Conflict,
            AdminErrorCodes.OrganizationNameAlreadyInUse, AdminErrorMessages.OrganizationNameAlreadyInUse);
        Assert.False(await OrganizationStoredAsync(name));
    }

    // --- #23 AC7 (O1): two concurrent renames to the same name ---

    [Fact]
    public async Task RenameOrganization_UniqueIndexRejectsSave_Returns409OrganizationNameAlreadyInUse()
    {
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, $"unique_rename_{Guid.NewGuid():N}");
        var name = $"Unique rename {Guid.NewGuid():N}";
        factory.ConcurrentSave.SavedFirstByAnotherRequest();

        var response = await platformAdmin.PutAsJsonAsync($"{OrganizationsEndpoint}/{organizationId}", new { Name = name });

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.Conflict,
            AdminErrorCodes.OrganizationNameAlreadyInUse, AdminErrorMessages.OrganizationNameAlreadyInUse);
        Assert.False(await OrganizationStoredAsync(name));
    }

    // --- AC3 (P2) ---

    [Fact]
    public async Task CreatePolicy_UniqueIndexRejectsSave_Returns409PolicyNameAlreadyInUse()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"unique_policy_{Guid.NewGuid():N}");
        factory.ConcurrentSave.SavedFirstByAnotherRequest();

        var response = await PostPolicyAsync(scenario.OrgAdmin, scenario.OrganizationId, "Unique policy");

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.Conflict,
            AdminErrorCodes.PolicyNameAlreadyInUse, AdminErrorMessages.PolicyNameAlreadyInUse);
        Assert.DoesNotContain(await StoredPoliciesAsync(factory), p => p.OrganizationId == scenario.OrganizationId);
    }

    // --- AC4 (C3) ---

    [Fact]
    public async Task CreateClearance_UniqueIndexRejectsSave_Returns409AlreadyGranted()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"unique_clearance_{Guid.NewGuid():N}");
        var policyId = await CreatePolicyAsync(scenario.OrgAdmin, "Unique clearance policy", scenario.OrganizationId);
        var (_, userId) = await CreateUserAsync(factory, $"unique_clearance_user_{Guid.NewGuid():N}");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, MemberRole);
        factory.ConcurrentSave.SavedFirstByAnotherRequest();

        var response = await PostClearanceAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, policyId);

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.Conflict,
            AdminErrorCodes.ClearanceAlreadyGranted, AdminErrorMessages.ClearanceAlreadyGranted);
        Assert.DoesNotContain(await StoredClearancesAsync(factory), c => c.UserId == userId && c.PolicyId == policyId);
    }

    // --- helpers ---

    private async Task<bool> UserStoredAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().Users.AnyAsync(u => u.Email == email);
    }

    private async Task<bool> OrganizationStoredAsync(string name)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().Organizations.AnyAsync(o => o.Name == name);
    }
}

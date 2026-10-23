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

namespace DataTrustHub.Features.Tests.Administration;

public class GetAndDeleteClearanceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task GetClearance_Existing_ReturnsIt()
    {
        var (admin, userId) = await CreateAdminAndUserAsync("clearance_get");
        var policyId = await CreatePolicyAsync(factory, admin, "Clearance get policy");
        var clearanceId = await CreateClearanceAsync(admin, userId, policyId);

        var clearance = await admin.GetFromJsonAsync<ClearanceResponse>($"{ClearancesEndpoint}/{clearanceId}");

        Assert.Equal((clearanceId, userId, policyId), (clearance!.Id, clearance.UserId, clearance.PolicyId));
    }

    [Fact]
    public async Task GetAndDeleteClearance_Missing_Return404()
    {
        var (admin, _) = await CreateAdminAndUserAsync("clearance_missing");
        var missingRoute = $"{ClearancesEndpoint}/{Guid.NewGuid()}";

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(missingRoute)).StatusCode);
        await ErrorAssert.HasErrorAsync(
            await admin.DeleteAsync(missingRoute), HttpStatusCode.NotFound,
            AdminErrorCodes.ClearanceNotFound, AdminErrorMessages.ClearanceNotFound);
    }

    [Fact]
    public async Task DeleteClearance_Existing_Returns204_AndUnblocksPolicyDeletion()
    {
        var (admin, userId) = await CreateAdminAndUserAsync("clearance_delete");
        var (organizationId, orgAdmin) = await CreateOrganizationWithOrgAdminAsync(factory, admin, "Clearance delete organization");
        var policyId = await CreatePolicyAsync(orgAdmin, "Clearance delete policy", organizationId);
        var clearanceId = await CreateClearanceAsync(admin, userId, policyId);
        var blocked = await orgAdmin.DeleteAsync(PolicyEndpoint(organizationId, policyId));

        var response = await admin.DeleteAsync($"{ClearancesEndpoint}/{clearanceId}");

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{ClearancesEndpoint}/{clearanceId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await orgAdmin.DeleteAsync(PolicyEndpoint(organizationId, policyId))).StatusCode);
    }

    [Fact]
    public async Task DeleteClearance_Existing_KeepsTheClassificationLevel()
    {
        const string levelName = "KeptAfterDelete";
        const int levelPriority = 9;
        var (admin, userId) = await CreateAdminAndUserAsync("clearance_keep_level");
        var policyId = await CreatePolicyAsync(factory, admin, "Clearance keep level policy");
        var created = await PostClearanceAsync(admin, userId, policyId, levelName, levelPriority);
        var clearanceId = (await created.Content.ReadFromJsonAsync<CreateClearanceResponse>())!.ClearanceId;

        var response = await admin.DeleteAsync($"{ClearancesEndpoint}/{clearanceId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        Assert.False(await db.Clearances.AnyAsync(c => c.Id == clearanceId));
        Assert.True(await db.ClassificationLevels.AnyAsync(l => l.Name == levelName && l.Priority == levelPriority));
    }

    [Fact]
    public async Task DeleteClearance_Anonymous_Returns401()
    {
        var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"{ClearancesEndpoint}/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task ClearanceEndpoints_RegularUser_Return403()
    {
        var user = await TestUsers.CreateClientAsync(factory, "clearance_get_regular@example.com");
        var route = $"{ClearancesEndpoint}/{Guid.NewGuid()}";

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync(route)).StatusCode);
    }

    // --- helpers ---

    private async Task<(HttpClient Admin, Guid UserId)> CreateAdminAndUserAsync(string prefix)
    {
        var admin = await TestUsers.CreateClientAsync(factory, $"{prefix}_admin@example.com", UserRoles.Admin);
        await TestUsers.CreateClientAsync(factory, $"{prefix}_user@example.com");
        return (admin, await TestUsers.GetIdAsync(factory, $"{prefix}_user@example.com"));
    }

    private record CreateClearanceResponse(Guid ClearanceId);

    private record ClearanceResponse(
        Guid Id, string Name, Guid UserId, Guid PolicyId, string ClassificationLevelName, int ClassificationLevelPriority);
}

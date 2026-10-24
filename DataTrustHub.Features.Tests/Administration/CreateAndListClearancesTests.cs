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

public class CreateAndListClearancesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CreateClearance_Valid_IsListedWithItsLevel()
    {
        var (admin, userId) = await CreateAdminAndUserAsync("clearance_create");
        var policyId = await CreatePolicyAsync(factory, admin, "Clearance create policy");

        var response = await PostClearanceAsync(admin, userId, policyId, "TopSecret", 3);
        var listed = await admin.GetFromJsonAsync<List<ClearanceResponse>>($"{ClearancesEndpoint}?userId={userId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var clearance = Assert.Single(listed!);
        Assert.Equal((policyId, "TopSecret", 3), (clearance.PolicyId, clearance.ClassificationLevelName, clearance.ClassificationLevelPriority));
    }

    [Fact]
    public async Task CreateClearance_SameLevelTwice_ReusesTheClassificationLevel()
    {
        var (admin, userId) = await CreateAdminAndUserAsync("clearance_reuse");
        await PostClearanceAsync(admin, userId, await CreatePolicyAsync(factory, admin, "Reuse policy 1"), "Reused", 7);
        await PostClearanceAsync(admin, userId, await CreatePolicyAsync(factory, admin, "Reuse policy 2"), "Reused", 7);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        Assert.Equal(1, await db.ClassificationLevels.CountAsync(l => l.Name == "Reused" && l.Priority == 7));
    }

    [Fact]
    public async Task CreateClearance_SameUserAndPolicyTwice_Returns409()
    {
        var (admin, userId) = await CreateAdminAndUserAsync("clearance_duplicate");
        var policyId = await CreatePolicyAsync(factory, admin, "Duplicate clearance policy");
        await PostClearanceAsync(admin, userId, policyId);

        var response = await PostClearanceAsync(admin, userId, policyId);

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AdminErrorCodes.ClearanceAlreadyGranted, AdminErrorMessages.ClearanceAlreadyGranted);
    }

    [Fact]
    public async Task CreateClearance_UnknownUserOrPolicy_Returns404()
    {
        var (admin, userId) = await CreateAdminAndUserAsync("clearance_unknown");
        var policyId = await CreatePolicyAsync(factory, admin, "Unknown refs policy");

        await ErrorAssert.HasErrorAsync(
            await PostClearanceAsync(admin, Guid.NewGuid(), policyId), HttpStatusCode.NotFound,
            AdminErrorCodes.ClearanceUserNotFound, AdminErrorMessages.ClearanceUserNotFound);
        await ErrorAssert.HasErrorAsync(
            await PostClearanceAsync(admin, userId, Guid.NewGuid()), HttpStatusCode.NotFound,
            AdminErrorCodes.ClearancePolicyNotFound, AdminErrorMessages.ClearancePolicyNotFound);
    }

    [Fact]
    public async Task CreateClearance_NegativePriority_Returns400()
    {
        var (admin, userId) = await CreateAdminAndUserAsync("clearance_invalid");
        var policyId = await CreatePolicyAsync(factory, admin, "Invalid clearance policy");

        var response = await PostClearanceAsync(admin, userId, policyId, "Secret", -1);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ClearanceEndpoints_RegularUser_Return403()
    {
        var user = await TestUsers.CreateClientAsync(factory, "clearance_regular@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(ClearancesEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostClearanceAsync(user, Guid.NewGuid(), Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task CreateClearance_Anonymous_Returns401()
    {
        var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostClearanceAsync(anonymous, Guid.NewGuid(), Guid.NewGuid())).StatusCode);
    }

    // --- helpers ---

    private async Task<(HttpClient Admin, Guid UserId)> CreateAdminAndUserAsync(string prefix)
    {
        var admin = await TestUsers.CreateClientAsync(factory, $"{prefix}_admin@example.com", UserRoles.Admin);
        await TestUsers.CreateClientAsync(factory, $"{prefix}_user@example.com");
        return (admin, await TestUsers.GetIdAsync(factory, $"{prefix}_user@example.com"));
    }

    private record ClearanceResponse(
        Guid Id, string Name, Guid UserId, Guid PolicyId, string ClassificationLevelName, int ClassificationLevelPriority);
}

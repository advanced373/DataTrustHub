using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #23 on SQL Server itself (a LocalDB database built by the migrations; skipped where LocalDB is missing): the
// collation is case-insensitive, so O1 must exclude the organization's own row (AC10), yet still find another
// organization's name in another letter case (AC2); the repository's UpdateAsync stores the new name (AC1)
public class RenameOrganizationSqlServerTests(LocalDbApiFactory factory) : IClassFixture<LocalDbApiFactory>
{
    [LocalDbFact]
    public async Task RenameOrganization_NewName_StoresIt()
    {
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, Prefix("sql_rename"));
        var newName = $"Renamed {Guid.NewGuid():N}";

        var response = await RenameAsync(platformAdmin, organizationId, newName);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(newName, await NameAsync(platformAdmin, organizationId));
    }

    [LocalDbFact]
    public async Task RenameOrganization_NewLetterCaseOfItsOwnName_Returns204AndStoresIt()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (platformAdmin, _) = await CreateOrganizationWithoutMembersAsync(factory, Prefix("sql_case"));
        var organizationId = await CreateOrganizationAsync(platformAdmin, $"Acme {suffix}");
        var upperCase = $"ACME {suffix}";

        var response = await RenameAsync(platformAdmin, organizationId, upperCase);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(upperCase, await NameAsync(platformAdmin, organizationId));
    }

    [LocalDbFact]
    public async Task RenameOrganization_TrailingSpaceOfItsOwnName_Returns204AndStoresIt()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (platformAdmin, _) = await CreateOrganizationWithoutMembersAsync(factory, Prefix("sql_space"));
        var organizationId = await CreateOrganizationAsync(platformAdmin, $"Acme {suffix}");
        var withTrailingSpace = $"Acme {suffix} ";

        var response = await RenameAsync(platformAdmin, organizationId, withTrailingSpace);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(withTrailingSpace, await NameAsync(platformAdmin, organizationId));
    }

    [LocalDbFact]
    public async Task RenameOrganization_OtherLetterCaseOfAnotherOrganizationsName_Returns409NameAlreadyInUse()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, Prefix("sql_taken"));
        await CreateOrganizationAsync(platformAdmin, $"Acme {suffix}");

        var response = await RenameAsync(platformAdmin, organizationId, $"ACME {suffix}");

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.Conflict,
            AdminErrorCodes.OrganizationNameAlreadyInUse, AdminErrorMessages.OrganizationNameAlreadyInUse);
    }

    // --- helpers ---

    private static string Prefix(string name) => $"{name}_{Guid.NewGuid():N}";

    private static Task<HttpResponseMessage> RenameAsync(HttpClient caller, Guid organizationId, string name) =>
        caller.PutAsJsonAsync($"{OrganizationsEndpoint}/{organizationId}", new { Name = name });

    private static async Task<string> NameAsync(HttpClient platformAdmin, Guid organizationId) =>
        (await platformAdmin.GetFromJsonAsync<OrganizationResponse>($"{OrganizationsEndpoint}/{organizationId}"))!.Name;

    private record OrganizationResponse(Guid Id, string Name);
}

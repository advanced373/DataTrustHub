using DataTrustHub.Models;
using DataTrustHub.Services.Api;
using DataTrustHub.Services.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.Controllers;

// An organization admin's page for their organization's audit trail (ADR-0016)
[Authorize]
[Route(OrganizationRoutes.Root)]
public class OrganizationAuditController(IAdminApiClient adminApi, IUserSessionService userSession)
    : OrganizationControllerBase(adminApi, userSession)
{
    // The filters optionally narrow the trail; only ids, action names and dates go in the query string
    // (ADR-0019). The page is shown only in an organization the user is an organization admin of
    [HttpGet(OrganizationRoutes.Audit)]
    public async Task<IActionResult> Index(
        Guid organizationId, [FromQuery] AuditFilterModel filter, CancellationToken cancellationToken)
    {
        var organization = await AdministeredOrganizationAsync(organizationId, cancellationToken);
        if (!organization.IsSuccess) return await BackToMyOrganizationsAsync(organization);

        var members = await AdminApi.ListMembersAsync(organizationId, cancellationToken);
        if (!members.IsSuccess) return await BackToMyOrganizationsAsync(members);

        var records = await AdminApi.ListOrganizationAuditRecordsAsync(
            organizationId, filter.ToApiFilter(), cancellationToken);
        // Validation messages stay on the page as form errors; any other failure, such as the API's 404,
        // goes back to the organizations list (401 included, through the session expiry)
        if (!records.IsSuccess && records.Details.Count == 0) return await BackToMyOrganizationsAsync(records);

        var users = members.Value!.Select(member => new AuditUserOption(member.UserId, member.Email));
        var trail = AuditTrailViewModel.From(
            records.Value, users, AuditActions.Organization, filter, ListErrorOf(records));
        return View(new OrganizationAuditViewModel(organizationId, organization.Value!.OrganizationName, trail));
    }
}

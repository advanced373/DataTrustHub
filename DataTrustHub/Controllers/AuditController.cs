using DataTrustHub.Models;
using DataTrustHub.Services.Api;
using DataTrustHub.Services.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.Controllers;

// The platform admin's page for the tenant-level audit trail (ADR-0016)
[Authorize(Roles = Constants.AdminRole)]
[Route(AdminRoutes.Audit)]
public class AuditController(IAdminApiClient adminApi, IUserSessionService userSession)
    : ApiControllerBase(userSession)
{
    // The filters optionally narrow the trail; only ids, action names and dates go in the query string (ADR-0019)
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] AuditFilterModel filter, CancellationToken cancellationToken)
    {
        var accounts = await adminApi.ListAccountsAsync(cancellationToken);
        if (accounts.IsUnauthorized) return await SessionExpiredAsync();

        var records = await adminApi.ListAuditRecordsAsync(filter.ToApiFilter(), cancellationToken);
        if (records.IsUnauthorized) return await SessionExpiredAsync();

        // A failed user list leaves the user filter empty and is shown as the list error
        var users = (accounts.Value ?? []).Select(account => new AuditUserOption(account.Id, account.Email));
        var errorMessage = ListErrorOf(records) ?? accounts.ErrorMessage;
        return View(AuditTrailViewModel.From(records.Value, users, AuditActions.TenantLevel, filter, errorMessage));
    }
}

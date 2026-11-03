using DataTrustHub.Services.Api;
using DataTrustHub.Services.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.Controllers;

// The pending registration requests, which the platform admin approves or rejects (ADR-0026)
[Authorize(Roles = Constants.AdminRole)]
[Route(AdminRoutes.RegistrationRequests)]
public class RegistrationRequestsController(IAdminApiClient adminApi, IUserSessionService userSession)
    : ApiControllerBase(userSession)
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        await ListViewAsync(await adminApi.ListRegistrationRequestsAsync(cancellationToken));

    // The email is posted with the form only for the message; the API acts on the id
    [HttpPost(AdminRoutes.Approve)]
    public async Task<IActionResult> Approve(Guid id, [FromForm] string email, CancellationToken cancellationToken)
    {
        var result = await adminApi.ApproveRegistrationRequestAsync(id, cancellationToken);
        return await RedirectWithOutcomeAsync(
            result, string.Format(Messages.RegistrationApproved, email), nameof(Index), ControllerNames.RegistrationRequests);
    }

    [HttpPost(AdminRoutes.Reject)]
    public async Task<IActionResult> Reject(Guid id, [FromForm] string email, CancellationToken cancellationToken)
    {
        var result = await adminApi.RejectRegistrationRequestAsync(id, cancellationToken);
        return await RedirectWithOutcomeAsync(
            result, string.Format(Messages.RegistrationRejected, email), nameof(Index), ControllerNames.RegistrationRequests);
    }
}

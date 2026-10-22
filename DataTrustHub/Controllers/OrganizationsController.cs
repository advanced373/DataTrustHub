using DataTrustHub.Models;
using DataTrustHub.Services.Api;
using DataTrustHub.Services.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.Controllers;

[Authorize(Roles = Constants.AdminRole)]
[Route(AdminRoutes.Organizations)]
public class OrganizationsController(IAdminApiClient adminApi, IUserSessionService userSession)
    : ApiControllerBase(userSession)
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        await ListViewAsync(await adminApi.ListOrganizationsAsync(cancellationToken));

    [HttpGet(AdminRoutes.Create)]
    public IActionResult Create() => View(new CreateOrganizationViewModel());

    [HttpPost(AdminRoutes.Create)]
    public async Task<IActionResult> Create(CreateOrganizationViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await adminApi.CreateOrganizationAsync(model.Name, cancellationToken);
        if (!result.IsSuccess) return await HandleFailureAsync(result, model);

        return RedirectWithSuccess(string.Format(Messages.OrganizationCreated, model.Name), nameof(Index), ControllerNames.Organizations);
    }

    [HttpPost(AdminRoutes.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await adminApi.DeleteOrganizationAsync(id, cancellationToken);
        return await RedirectWithOutcomeAsync(result, Messages.OrganizationDeleted, nameof(Index), ControllerNames.Organizations);
    }
}

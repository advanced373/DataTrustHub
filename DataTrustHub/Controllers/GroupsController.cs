using DataTrustHub.Models;
using DataTrustHub.Services.Api;
using DataTrustHub.Services.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.Controllers;

[Authorize(Roles = Constants.AdminRole)]
[Route(AdminRoutes.Groups)]
public class GroupsController(IAdminApiClient adminApi, IUserSessionService userSession)
    : ApiControllerBase(userSession)
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        await ListViewAsync(await adminApi.ListGroupsAsync(cancellationToken));

    [HttpGet(AdminRoutes.Create)]
    public IActionResult Create() => View(new CreateGroupViewModel());

    [HttpPost(AdminRoutes.Create)]
    public async Task<IActionResult> Create(CreateGroupViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await adminApi.CreateGroupAsync(model.Name, cancellationToken);
        if (!result.IsSuccess) return await HandleFailureAsync(result, model);

        return RedirectWithSuccess(string.Format(Messages.GroupCreated, model.Name), nameof(Index), ControllerNames.Groups);
    }

    [HttpPost(AdminRoutes.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await adminApi.DeleteGroupAsync(id, cancellationToken);
        return await RedirectWithOutcomeAsync(result, Messages.GroupDeleted, nameof(Index), ControllerNames.Groups);
    }
}

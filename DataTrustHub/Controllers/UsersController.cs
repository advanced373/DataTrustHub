using DataTrustHub.Models;
using DataTrustHub.Services.Api;
using DataTrustHub.Services.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.Controllers;

[Authorize(Roles = Constants.AdminRole)]
[Route(AdminRoutes.Users)]
public class UsersController(IAdminApiClient adminApi, IUserSessionService userSession)
    : ApiControllerBase(userSession)
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        await ListViewAsync(await adminApi.ListAccountsAsync(cancellationToken));

    [HttpGet(AdminRoutes.Create)]
    public IActionResult Create() => View(new CreateAccountViewModel());

    [HttpPost(AdminRoutes.Create)]
    public async Task<IActionResult> Create(CreateAccountViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await adminApi.CreateAccountAsync(model.Email, model.Password, cancellationToken);
        if (!result.IsSuccess) return await HandleFailureAsync(result, model);

        return RedirectWithSuccess(string.Format(Messages.AccountCreated, model.Email), nameof(Index), ControllerNames.Users);
    }

    [HttpPost(AdminRoutes.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await adminApi.DeleteAccountAsync(id, cancellationToken);
        return await RedirectWithOutcomeAsync(result, Messages.AccountDeleted, nameof(Index), ControllerNames.Users);
    }
}

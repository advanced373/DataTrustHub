using DataTrustHub.Models;
using DataTrustHub.Services.Api;
using DataTrustHub.Services.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.Controllers;

[Authorize(Roles = Constants.AdminRole)]
public class AdminController(IAdminApiClient adminApi, IUserSessionService userSession)
    : ApiControllerBase(userSession)
{
    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public IActionResult CreateAccount() => View(new CreateAccountViewModel());

    [HttpPost]
    public async Task<IActionResult> CreateAccount(CreateAccountViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await adminApi.CreateAccountAsync(model.Email, model.Password, cancellationToken);
        if (!result.IsSuccess) return await HandleFailureAsync(result, model);

        return RedirectToAdmin(string.Format(Messages.AccountCreated, result.Value));
    }

    private IActionResult RedirectToAdmin(string message) =>
        RedirectWithSuccess(message, nameof(Index), ControllerNames.Admin);
}

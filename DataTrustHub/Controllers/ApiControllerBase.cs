using DataTrustHub.Services.Api;
using DataTrustHub.Services.Session;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.Controllers;

// Shared handling of API results for controllers that call the DataTrustHub API
public abstract class ApiControllerBase(IUserSessionService userSession) : Controller
{
    protected IUserSessionService UserSession { get; } = userSession;

    protected async Task<IActionResult> HandleFailureAsync<T>(ApiResult<T> result, object model)
    {
        if (result.IsUnauthorized) return await SessionExpiredAsync();

        ModelState.AddModelError(string.Empty, result.ErrorMessage ?? Messages.RequestFailed);
        return View(model);
    }

    protected IActionResult RedirectWithSuccess(string message, string action, string controller)
    {
        TempData[TempDataKeys.Success] = message;
        return RedirectToAction(action, controller);
    }

    private async Task<IActionResult> SessionExpiredAsync()
    {
        await UserSession.SignOutAsync();
        TempData[TempDataKeys.Warning] = Messages.SessionExpired;
        return RedirectToAction(nameof(AccountController.Login), ControllerNames.Account);
    }
}

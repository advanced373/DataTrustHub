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

        AddApiErrors(result);
        return View(model);
    }

    // Shows the individual validation messages when the API sends them, otherwise the summary message
    private void AddApiErrors<T>(ApiResult<T> result)
    {
        IEnumerable<string> messages = result.Details.Count > 0
            ? result.Details
            : [result.ErrorMessage ?? Messages.RequestFailed];

        foreach (var message in messages)
            ModelState.AddModelError(string.Empty, message);
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

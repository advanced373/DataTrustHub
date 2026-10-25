using DataTrustHub.Models;
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
    protected void AddApiErrors<T>(ApiResult<T> result)
    {
        IEnumerable<string> messages = result.Details.Count > 0
            ? result.Details
            : [result.ErrorMessage ?? Messages.RequestFailed];

        foreach (var message in messages)
            ModelState.AddModelError(string.Empty, message);
    }

    protected IActionResult RedirectWithSuccess(
        string message, string action, string controller, object? routeValues = null)
    {
        TempData[TempDataKeys.Success] = message;
        return RedirectToAction(action, controller, routeValues);
    }

    // Renders a list page; a failed load shows the error above an empty list
    protected async Task<IActionResult> ListViewAsync<T>(ApiResult<IReadOnlyList<T>> result)
    {
        if (result.IsUnauthorized) return await SessionExpiredAsync();

        var model = result.IsSuccess
            ? new ListViewModel<T>(result.Value!)
            : new ListViewModel<T>([], result.ErrorMessage);
        return View(model);
    }

    // A list loaded with filters: the API's validation messages, such as a range that ends before it starts,
    // are shown as form errors above the filters; any other failure is returned for the page's list error
    protected string? ListErrorOf<T>(ApiResult<T> result)
    {
        if (result.IsSuccess) return null;
        if (result.Details.Count == 0) return result.ErrorMessage;

        AddApiErrors(result);
        return null;
    }

    // For actions without a form, such as deletes: the outcome is shown as an alert after the redirect
    protected async Task<IActionResult> RedirectWithOutcomeAsync<T>(
        ApiResult<T> result, string successMessage, string action, string controller, object? routeValues = null)
    {
        if (result.IsUnauthorized) return await SessionExpiredAsync();
        if (result.IsSuccess) return RedirectWithSuccess(successMessage, action, controller, routeValues);

        TempData[TempDataKeys.Warning] = result.ErrorMessage;
        return RedirectToAction(action, controller, routeValues);
    }

    protected async Task<IActionResult> SessionExpiredAsync()
    {
        await UserSession.SignOutAsync();
        TempData[TempDataKeys.Warning] = Messages.SessionExpired;
        return RedirectToAction(nameof(AccountController.Login), ControllerNames.Account);
    }
}

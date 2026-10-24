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

    [HttpGet(AdminRoutes.AppointAdmin)]
    public async Task<IActionResult> AppointAdmin(Guid id, CancellationToken cancellationToken) =>
        await AppointAdminViewAsync(id, new AppointOrganizationAdminViewModel(), cancellationToken);

    // The email is resolved through the platform's account list, so it never goes in a URL (ADR-0019)
    [HttpPost(AdminRoutes.AppointAdmin)]
    public async Task<IActionResult> AppointAdmin(
        Guid id, AppointOrganizationAdminViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return await AppointAdminViewAsync(id, model, cancellationToken);

        var accounts = await adminApi.ListAccountsAsync(cancellationToken);
        if (!accounts.IsSuccess) return await AppointAdminFailedAsync(id, accounts, model, cancellationToken);

        var email = model.Email.Trim();
        var account = accounts.Value!.FirstOrDefault(candidate =>
            string.Equals(candidate.Email, email, StringComparison.OrdinalIgnoreCase));
        if (account is null)
        {
            ModelState.AddModelError(nameof(model.Email), Messages.NoUserWithEmail);
            return await AppointAdminViewAsync(id, model, cancellationToken);
        }

        var result = await adminApi.AppointOrganizationAdminAsync(id, account.Id, cancellationToken);
        if (!result.IsSuccess) return await AppointAdminFailedAsync(id, result, model, cancellationToken);

        var message = string.Format(Messages.OrganizationAdminAppointed, account.Email);
        return RedirectWithSuccess(message, nameof(Index), ControllerNames.Organizations);
    }

    // The form needs the organization's name; an unknown organization goes back to the list
    private async Task<IActionResult> AppointAdminViewAsync(
        Guid id, AppointOrganizationAdminViewModel model, CancellationToken cancellationToken)
    {
        var organization = await adminApi.GetOrganizationAsync(id, cancellationToken);
        if (!organization.IsSuccess)
            return await RedirectWithOutcomeAsync(organization, string.Empty, nameof(Index), ControllerNames.Organizations);

        model.OrganizationId = id;
        model.OrganizationName = organization.Value!.Name;
        return View(nameof(AppointAdmin), model);
    }

    private async Task<IActionResult> AppointAdminFailedAsync<T>(
        Guid id, ApiResult<T> result, AppointOrganizationAdminViewModel model, CancellationToken cancellationToken)
    {
        if (result.IsUnauthorized) return await SessionExpiredAsync();

        AddApiErrors(result);
        return await AppointAdminViewAsync(id, model, cancellationToken);
    }
}

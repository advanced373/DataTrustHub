using DataTrustHub.Models;
using DataTrustHub.Services.Api;
using DataTrustHub.Services.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.Controllers;

[Authorize(Roles = Constants.AdminRole)]
[Route(AdminRoutes.Policies)]
public class PoliciesController(IAdminApiClient adminApi, IUserSessionService userSession)
    : ApiControllerBase(userSession)
{
    // organizationId optionally narrows the list to one organization's policies
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid? organizationId, CancellationToken cancellationToken)
    {
        var organizations = await adminApi.ListOrganizationsAsync(cancellationToken);
        var policies = await adminApi.ListPoliciesAsync(organizationId, cancellationToken);
        if (organizations.IsUnauthorized || policies.IsUnauthorized) return await SessionExpiredAsync();

        var error = organizations.ErrorMessage ?? policies.ErrorMessage;
        var organizationList = organizations.Value ?? [];
        var rows = error is null ? ToRows(policies.Value!, organizationList) : [];
        return View(new PolicyListViewModel(rows, organizationList, organizationId, error));
    }

    [HttpGet(AdminRoutes.Create)]
    public async Task<IActionResult> Create(Guid? organizationId, CancellationToken cancellationToken)
    {
        var model = new CreatePolicyViewModel { OrganizationId = organizationId };
        return await CreateViewAsync(model, cancellationToken);
    }

    [HttpPost(AdminRoutes.Create)]
    public async Task<IActionResult> Create(CreatePolicyViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return await CreateViewAsync(model, cancellationToken);

        var result = await adminApi.CreatePolicyAsync(model.Name, model.OrganizationId!.Value, cancellationToken);
        if (!result.IsSuccess)
        {
            model.Organizations = (await adminApi.ListOrganizationsAsync(cancellationToken)).Value ?? [];
            return await HandleFailureAsync(result, model);
        }

        return RedirectWithSuccess(string.Format(Messages.PolicyCreated, model.Name), nameof(Index), ControllerNames.Policies);
    }

    [HttpPost(AdminRoutes.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await adminApi.DeletePolicyAsync(id, cancellationToken);
        return await RedirectWithOutcomeAsync(result, Messages.PolicyDeleted, nameof(Index), ControllerNames.Policies);
    }

    private async Task<IActionResult> CreateViewAsync(CreatePolicyViewModel model, CancellationToken cancellationToken)
    {
        var organizations = await adminApi.ListOrganizationsAsync(cancellationToken);
        if (organizations.IsUnauthorized) return await SessionExpiredAsync();
        if (!organizations.IsSuccess) ModelState.AddModelError(string.Empty, organizations.ErrorMessage!);

        model.Organizations = organizations.Value ?? [];
        return View(nameof(Create), model);
    }

    private static List<PolicyRow> ToRows(IReadOnlyList<PolicyResponse> policies, IReadOnlyList<OrganizationResponse> organizations)
    {
        var organizationNames = organizations.ToDictionary(organization => organization.Id, organization => organization.Name);
        return policies
            .Select(policy => new PolicyRow(
                policy.Id,
                policy.Name,
                policy.OrganizationId,
                organizationNames.GetValueOrDefault(policy.OrganizationId, Messages.UnknownOrganization)))
            .ToList();
    }
}

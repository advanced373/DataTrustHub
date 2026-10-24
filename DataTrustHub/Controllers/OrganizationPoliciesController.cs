using DataTrustHub.Models;
using DataTrustHub.Services.Api;
using DataTrustHub.Services.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.Controllers;

// An organization admin's pages for their organization's policies (ADR-0014, C6)
[Authorize]
[Route(OrganizationRoutes.Root)]
public class OrganizationPoliciesController(IAdminApiClient adminApi, IUserSessionService userSession)
    : OrganizationControllerBase(adminApi, userSession)
{
    [HttpGet(OrganizationRoutes.Policies)]
    public async Task<IActionResult> Index(Guid organizationId, CancellationToken cancellationToken)
    {
        var policies = await AdminApi.ListPoliciesAsync(organizationId, cancellationToken);
        if (!policies.IsSuccess) return await BackToMyOrganizationsAsync(policies);

        var organizationName = await OrganizationNameAsync(organizationId, cancellationToken);
        var sorted = policies.Value!.OrderBy(policy => policy.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return View(new PoliciesViewModel(organizationId, organizationName, sorted));
    }

    [HttpGet(OrganizationRoutes.CreatePolicy)]
    public async Task<IActionResult> Create(Guid organizationId, CancellationToken cancellationToken) =>
        await CreateViewAsync(organizationId, new CreatePolicyViewModel(), cancellationToken);

    // A refusal, such as a name already in use, is shown on the form with the API's message
    [HttpPost(OrganizationRoutes.CreatePolicy)]
    public async Task<IActionResult> Create(
        Guid organizationId, CreatePolicyViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return await CreateViewAsync(organizationId, model, cancellationToken);

        var name = model.Name.Trim();
        var result = await AdminApi.CreatePolicyAsync(organizationId, name, cancellationToken);
        if (!result.IsSuccess)
            return await ShowFormWithApiErrorsAsync(result, () => CreateViewAsync(organizationId, model, cancellationToken));

        var message = string.Format(Messages.PolicyCreated, name);
        return RedirectWithSuccess(message, nameof(Index), ControllerNames.OrganizationPolicies, new { organizationId });
    }

    // A refusal, such as a policy that still has clearances, is shown as an alert on the policies page
    [HttpPost(OrganizationRoutes.DeletePolicy)]
    public async Task<IActionResult> Delete(Guid organizationId, Guid policyId, CancellationToken cancellationToken)
    {
        var result = await AdminApi.DeletePolicyAsync(organizationId, policyId, cancellationToken);
        return await RedirectWithOutcomeAsync(
            result, Messages.PolicyDeleted, nameof(Index), ControllerNames.OrganizationPolicies, new { organizationId });
    }

    // The form is shown only in an organization the user is an organization admin of
    private async Task<IActionResult> CreateViewAsync(
        Guid organizationId, CreatePolicyViewModel model, CancellationToken cancellationToken)
    {
        var organization = await AdministeredOrganizationAsync(organizationId, cancellationToken);
        if (!organization.IsSuccess) return await BackToMyOrganizationsAsync(organization);

        model.OrganizationId = organizationId;
        model.OrganizationName = organization.Value!.OrganizationName;
        return View(nameof(Create), model);
    }
}

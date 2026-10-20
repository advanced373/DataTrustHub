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
    // groupId optionally narrows the list to one group's policies
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid? groupId, CancellationToken cancellationToken)
    {
        var groups = await adminApi.ListGroupsAsync(cancellationToken);
        var policies = await adminApi.ListPoliciesAsync(groupId, cancellationToken);
        if (groups.IsUnauthorized || policies.IsUnauthorized) return await SessionExpiredAsync();

        var error = groups.ErrorMessage ?? policies.ErrorMessage;
        var groupList = groups.Value ?? [];
        var rows = error is null ? ToRows(policies.Value!, groupList) : [];
        return View(new PolicyListViewModel(rows, groupList, groupId, error));
    }

    [HttpGet(AdminRoutes.Create)]
    public async Task<IActionResult> Create(Guid? groupId, CancellationToken cancellationToken)
    {
        var model = new CreatePolicyViewModel { OrganizationId = groupId };
        return await CreateViewAsync(model, cancellationToken);
    }

    [HttpPost(AdminRoutes.Create)]
    public async Task<IActionResult> Create(CreatePolicyViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return await CreateViewAsync(model, cancellationToken);

        var result = await adminApi.CreatePolicyAsync(model.Name, model.OrganizationId!.Value, cancellationToken);
        if (!result.IsSuccess)
        {
            model.Groups = (await adminApi.ListGroupsAsync(cancellationToken)).Value ?? [];
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
        var groups = await adminApi.ListGroupsAsync(cancellationToken);
        if (groups.IsUnauthorized) return await SessionExpiredAsync();
        if (!groups.IsSuccess) ModelState.AddModelError(string.Empty, groups.ErrorMessage!);

        model.Groups = groups.Value ?? [];
        return View(nameof(Create), model);
    }

    private static List<PolicyRow> ToRows(IReadOnlyList<PolicyResponse> policies, IReadOnlyList<GroupResponse> groups)
    {
        var groupNames = groups.ToDictionary(group => group.Id, group => group.Name);
        return policies
            .Select(policy => new PolicyRow(
                policy.Id,
                policy.Name,
                policy.OrganizationId,
                groupNames.GetValueOrDefault(policy.OrganizationId, Messages.UnknownGroup)))
            .ToList();
    }
}

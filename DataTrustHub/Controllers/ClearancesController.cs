using DataTrustHub.Models;
using DataTrustHub.Services.Api;
using DataTrustHub.Services.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.Controllers;

[Authorize(Roles = Constants.AdminRole)]
[Route(AdminRoutes.Clearances)]
public class ClearancesController(IAdminApiClient adminApi, IUserSessionService userSession)
    : ApiControllerBase(userSession)
{
    // userId optionally narrows the list to one user's clearances
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid? userId, CancellationToken cancellationToken)
    {
        var lookups = await LoadLookupsAsync(cancellationToken);
        var clearances = await adminApi.ListClearancesAsync(userId, cancellationToken);
        if (lookups.IsUnauthorized || clearances.IsUnauthorized) return await SessionExpiredAsync();

        var error = lookups.Error ?? clearances.ErrorMessage;
        var rows = error is null ? ToRows(clearances.Value!, lookups) : [];
        return View(new ClearanceListViewModel(rows, lookups.Users, userId, error));
    }

    [HttpGet(AdminRoutes.Create)]
    public async Task<IActionResult> Create(Guid? userId, CancellationToken cancellationToken) =>
        await GrantViewAsync(new GrantClearanceViewModel { UserId = userId }, cancellationToken);

    [HttpPost(AdminRoutes.Create)]
    public async Task<IActionResult> Create(GrantClearanceViewModel model, CancellationToken cancellationToken)
    {
        var level = ClassificationLevels.All.FirstOrDefault(l => l.Priority == model.LevelPriority);
        if (level.Name is null) ModelState.AddModelError(nameof(model.LevelPriority), Messages.LevelRequired);
        if (!ModelState.IsValid) return await GrantViewAsync(model, cancellationToken);

        var lookups = await LoadLookupsAsync(cancellationToken);
        var grant = BuildGrant(model, level, lookups);
        var result = await adminApi.GrantClearanceAsync(grant, cancellationToken);
        if (!result.IsSuccess) return await HandleFailureAsync(result, WithOptions(model, lookups));

        var email = lookups.Users.FirstOrDefault(u => u.Id == grant.UserId)?.Email ?? Messages.UnknownUser;
        TempData[TempDataKeys.Success] = string.Format(Messages.ClearanceGranted, email);
        return RedirectToAction(nameof(Index), new { userId = grant.UserId });
    }

    [HttpPost(AdminRoutes.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await adminApi.RevokeClearanceAsync(id, cancellationToken);
        return await RedirectWithOutcomeAsync(result, Messages.ClearanceRevoked, nameof(Index), ControllerNames.Clearances);
    }

    private async Task<IActionResult> GrantViewAsync(GrantClearanceViewModel model, CancellationToken cancellationToken)
    {
        var lookups = await LoadLookupsAsync(cancellationToken);
        if (lookups.IsUnauthorized) return await SessionExpiredAsync();
        if (lookups.Error is not null) ModelState.AddModelError(string.Empty, lookups.Error);

        return View(nameof(Create), WithOptions(model, lookups));
    }

    // Users and policies (labelled with their group) for the dropdowns and the list's names
    private async Task<Lookups> LoadLookupsAsync(CancellationToken cancellationToken)
    {
        var users = await adminApi.ListAccountsAsync(cancellationToken);
        var policies = await adminApi.ListPoliciesAsync(null, cancellationToken);
        var groups = await adminApi.ListOrganizationsAsync(cancellationToken);

        var groupNames = (groups.Value ?? []).ToDictionary(g => g.Id, g => g.Name);
        var policyOptions = (policies.Value ?? [])
            .Select(p => new PolicyOption(p.Id, $"{p.Name} ({groupNames.GetValueOrDefault(p.OrganizationId, Messages.UnknownGroup)})"))
            .OrderBy(p => p.Label)
            .ToList();

        return new Lookups(
            users.Value ?? [],
            policyOptions,
            users.IsUnauthorized || policies.IsUnauthorized || groups.IsUnauthorized,
            users.ErrorMessage ?? policies.ErrorMessage ?? groups.ErrorMessage);
    }

    private static ClearanceGrant BuildGrant(
        GrantClearanceViewModel model, (string Name, int Priority) level, Lookups lookups)
    {
        var policyLabel = lookups.Policies.FirstOrDefault(p => p.Id == model.PolicyId)?.Label ?? Messages.UnknownPolicy;
        var name = string.IsNullOrWhiteSpace(model.Name)
            ? string.Format(Messages.DefaultClearanceName, level.Name, policyLabel)
            : model.Name.Trim();

        return new ClearanceGrant(model.UserId!.Value, model.PolicyId!.Value, name, level.Name, level.Priority);
    }

    private static GrantClearanceViewModel WithOptions(GrantClearanceViewModel model, Lookups lookups)
    {
        model.Users = lookups.Users;
        model.Policies = lookups.Policies;
        return model;
    }

    private static List<ClearanceRow> ToRows(IReadOnlyList<ClearanceResponse> clearances, Lookups lookups)
    {
        var emails = lookups.Users.ToDictionary(u => u.Id, u => u.Email);
        var policyLabels = lookups.Policies.ToDictionary(p => p.Id, p => p.Label);

        return clearances
            .Select(c => new ClearanceRow(
                c.Id,
                emails.GetValueOrDefault(c.UserId, Messages.UnknownUser),
                policyLabels.GetValueOrDefault(c.PolicyId, Messages.UnknownPolicy),
                c.Name,
                c.ClassificationLevelName,
                c.ClassificationLevelPriority))
            .OrderBy(row => row.UserEmail)
            .ThenBy(row => row.PolicyLabel)
            .ToList();
    }

    private sealed record Lookups(
        IReadOnlyList<AccountResponse> Users,
        IReadOnlyList<PolicyOption> Policies,
        bool IsUnauthorized,
        string? Error);
}

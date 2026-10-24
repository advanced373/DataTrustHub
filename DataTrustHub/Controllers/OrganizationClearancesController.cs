using DataTrustHub.Models;
using DataTrustHub.Services.Api;
using DataTrustHub.Services.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.Controllers;

// An organization admin's pages for the clearances on their organization's policies (ADR-0014, C5, C6, C8)
[Authorize]
[Route(OrganizationRoutes.Root)]
public class OrganizationClearancesController(IAdminApiClient adminApi, IUserSessionService userSession)
    : OrganizationControllerBase(adminApi, userSession)
{
    // userId and policyId optionally narrow the list; only ids go in the query string (ADR-0019)
    [HttpGet(OrganizationRoutes.Clearances)]
    public async Task<IActionResult> Index(
        Guid organizationId, Guid? userId, Guid? policyId, CancellationToken cancellationToken)
    {
        var lookups = await LoadLookupsAsync(organizationId, cancellationToken);
        if (!lookups.IsSuccess) return await BackToMyOrganizationsAsync(lookups);

        var clearances = await AdminApi.ListClearancesAsync(organizationId, userId, policyId, cancellationToken);
        if (!clearances.IsSuccess) return await BackToMyOrganizationsAsync(clearances);

        var organizationName = await OrganizationNameAsync(organizationId, cancellationToken);
        var found = lookups.Value!;
        var rows = ToRows(clearances.Value!, found);
        return View(new ClearancesViewModel(
            organizationId, organizationName, rows, found.Members, found.Policies, userId, policyId));
    }

    [HttpGet(OrganizationRoutes.GrantClearance)]
    public async Task<IActionResult> Grant(Guid organizationId, CancellationToken cancellationToken) =>
        await GrantViewAsync(organizationId, new GrantClearanceViewModel(), cancellationToken);

    // Every refusal, such as a user who already holds a clearance on the policy, is shown on the form
    [HttpPost(OrganizationRoutes.GrantClearance)]
    public async Task<IActionResult> Grant(
        Guid organizationId, GrantClearanceViewModel model, CancellationToken cancellationToken)
    {
        var isKnownLevel = TryFindLevel(model.LevelPriority, out var level);
        if (model.LevelPriority is not null && !isKnownLevel)
            ModelState.AddModelError(nameof(model.LevelPriority), Messages.LevelRequired);
        if (!ModelState.IsValid) return await GrantViewAsync(organizationId, model, cancellationToken);

        Task<IActionResult> ShowForm() => GrantViewAsync(organizationId, model, cancellationToken);
        var lookups = await LoadLookupsAsync(organizationId, cancellationToken);
        if (!lookups.IsSuccess) return await ShowFormWithApiErrorsAsync(lookups, ShowForm);

        var grant = BuildGrant(model, level, lookups.Value!);
        var result = await AdminApi.GrantClearanceAsync(organizationId, grant, cancellationToken);
        if (!result.IsSuccess) return await ShowFormWithApiErrorsAsync(result, ShowForm);

        var email = EmailOf(grant.UserId, lookups.Value!);
        var message = string.Format(Messages.ClearanceGranted, email);
        return RedirectWithSuccess(message, nameof(Index), ControllerNames.OrganizationClearances, new { organizationId });
    }

    [HttpPost(OrganizationRoutes.RevokeClearance)]
    public async Task<IActionResult> Revoke(Guid organizationId, Guid clearanceId, CancellationToken cancellationToken)
    {
        var result = await AdminApi.RevokeClearanceAsync(organizationId, clearanceId, cancellationToken);
        return await RedirectWithOutcomeAsync(
            result, Messages.ClearanceRevoked, nameof(Index), ControllerNames.OrganizationClearances, new { organizationId });
    }

    // The form is shown only in an organization the user is an organization admin of. The signed-in user
    // is not offered as the grantee, because an organization admin cannot grant themselves (C8)
    private async Task<IActionResult> GrantViewAsync(
        Guid organizationId, GrantClearanceViewModel model, CancellationToken cancellationToken)
    {
        var organization = await AdministeredOrganizationAsync(organizationId, cancellationToken);
        if (!organization.IsSuccess) return await BackToMyOrganizationsAsync(organization);

        var lookups = await LoadLookupsAsync(organizationId, cancellationToken);
        if (!lookups.IsSuccess) return await BackToMyOrganizationsAsync(lookups);

        var signedInUserId = SignedInUserId();
        model.OrganizationId = organizationId;
        model.OrganizationName = organization.Value!.OrganizationName;
        model.Grantees = lookups.Value!.Members.Where(member => member.UserId != signedInUserId).ToList();
        model.Policies = lookups.Value!.Policies;
        return View(nameof(Grant), model);
    }

    // The organization's members (for emails and the grantee list) and policies, each sorted by name
    private async Task<ApiResult<Lookups>> LoadLookupsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var members = await AdminApi.ListMembersAsync(organizationId, cancellationToken);
        if (!members.IsSuccess) return members.Map(_ => Lookups.Empty);

        var policies = await AdminApi.ListPoliciesAsync(organizationId, cancellationToken);
        return policies.Map(found => new Lookups(
            members.Value!.OrderBy(member => member.Email, StringComparer.OrdinalIgnoreCase).ToList(),
            found.OrderBy(policy => policy.Name, StringComparer.CurrentCultureIgnoreCase).ToList()));
    }

    private static ClearanceGrant BuildGrant(
        GrantClearanceViewModel model, (string Name, int Priority) level, Lookups lookups)
    {
        var policyName = lookups.Policies.FirstOrDefault(policy => policy.Id == model.PolicyId)?.Name ?? Messages.UnknownPolicy;
        var name = string.IsNullOrWhiteSpace(model.Name)
            ? DefaultName(level.Name, policyName)
            : model.Name.Trim();

        return new ClearanceGrant(model.UserId!.Value, model.PolicyId!.Value, name, level.Name, level.Priority);
    }

    // "<level> on <policy>", cut to the API's maximum name length for a long policy name
    private static string DefaultName(string levelName, string policyName)
    {
        var name = string.Format(Messages.DefaultClearanceName, levelName, policyName);
        return name.Length > Constants.MaxNameLength ? name[..Constants.MaxNameLength] : name;
    }

    // One of the standard classification levels, found by its priority
    private static bool TryFindLevel(int? priority, out (string Name, int Priority) level)
    {
        var matches = ClassificationLevels.All.Where(candidate => candidate.Priority == priority).ToList();
        level = matches.Count > 0 ? matches[0] : default;
        return matches.Count > 0;
    }

    private static string EmailOf(Guid userId, Lookups lookups) =>
        lookups.Members.FirstOrDefault(member => member.UserId == userId)?.Email ?? Messages.UnknownMember;

    // Emails and policy names instead of ids; a clearance always belongs to a current member, but the
    // fallbacks keep the page readable if the lists change between the calls
    private static List<ClearanceRow> ToRows(IReadOnlyList<ClearanceResponse> clearances, Lookups lookups)
    {
        var emails = lookups.Members.ToDictionary(member => member.UserId, member => member.Email);
        var policyNames = lookups.Policies.ToDictionary(policy => policy.Id, policy => policy.Name);

        return clearances
            .Select(clearance => new ClearanceRow(
                clearance.Id,
                emails.GetValueOrDefault(clearance.UserId, Messages.UnknownMember),
                policyNames.GetValueOrDefault(clearance.PolicyId, Messages.UnknownPolicy),
                clearance.Name,
                clearance.ClassificationLevelName,
                clearance.ClassificationLevelPriority))
            .OrderBy(row => row.MemberEmail, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.PolicyName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private sealed record Lookups(IReadOnlyList<MemberResponse> Members, IReadOnlyList<PolicyResponse> Policies)
    {
        internal static readonly Lookups Empty = new([], []);
    }
}

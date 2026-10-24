using System.Security.Claims;
using DataTrustHub.Models;
using DataTrustHub.Services.Api;
using DataTrustHub.Services.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.Controllers;

// The signed-in user's organizations, and the member pages of those they are an organization admin of.
// The API decides who may do what: it answers 404 Organization.NotFound outside the needed membership
[Authorize]
[Route(OrganizationRoutes.Root)]
public class MyOrganizationsController(IAdminApiClient adminApi, IUserSessionService userSession)
    : ApiControllerBase(userSession)
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        await ListViewAsync(await adminApi.ListMyOrganizationsAsync(cancellationToken));

    [HttpPost(OrganizationRoutes.Leave)]
    public async Task<IActionResult> Leave(Guid organizationId, CancellationToken cancellationToken)
    {
        var result = await adminApi.LeaveOrganizationAsync(organizationId, cancellationToken);
        return await RedirectWithOutcomeAsync(result, Messages.OrganizationLeft, nameof(Index), ControllerNames.MyOrganizations);
    }

    // A failed load, such as the API's 404 outside the organization, goes back to the organizations list
    [HttpGet(OrganizationRoutes.Members)]
    public async Task<IActionResult> Members(Guid organizationId, CancellationToken cancellationToken)
    {
        var members = await adminApi.ListMembersAsync(organizationId, cancellationToken);
        if (!members.IsSuccess)
            return await RedirectWithOutcomeAsync(members, string.Empty, nameof(Index), ControllerNames.MyOrganizations);

        var organizations = await adminApi.ListMyOrganizationsAsync(cancellationToken);
        var organizationName = organizations.Value?
            .FirstOrDefault(organization => organization.OrganizationId == organizationId)?
            .OrganizationName;
        return View(new MembersViewModel(organizationId, organizationName, members.Value!, SignedInUserId()));
    }

    [HttpGet(OrganizationRoutes.AddMember)]
    public async Task<IActionResult> AddMember(Guid organizationId, CancellationToken cancellationToken) =>
        await AddMemberViewAsync(organizationId, new AddMemberViewModel(), cancellationToken);

    // Finds the user by email first, so an existing membership is reported without trying to add it
    [HttpPost(OrganizationRoutes.AddMember)]
    public async Task<IActionResult> AddMember(
        Guid organizationId, AddMemberViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return await AddMemberViewAsync(organizationId, model, cancellationToken);

        var user = await adminApi.FindUserByEmailAsync(organizationId, model.Email.Trim(), cancellationToken);
        if (!user.IsSuccess) return await AddMemberFailedAsync(organizationId, user, model, cancellationToken);

        var found = user.Value!;
        if (found.IsMember)
        {
            var label = MembershipRoles.LabelOf(found.Role);
            ModelState.AddModelError(string.Empty, string.Format(Messages.AlreadyMember, found.Email, label));
            return await AddMemberViewAsync(organizationId, model, cancellationToken);
        }

        var added = await adminApi.AddMemberAsync(organizationId, found.UserId, model.Role, cancellationToken);
        if (!added.IsSuccess) return await AddMemberFailedAsync(organizationId, added, model, cancellationToken);

        var message = string.Format(Messages.MemberAdded, found.Email, MembershipRoles.LabelOf(model.Role));
        return RedirectWithSuccess(message, nameof(Members), ControllerNames.MyOrganizations, new { organizationId });
    }

    [HttpPost(OrganizationRoutes.ChangeRole)]
    public async Task<IActionResult> ChangeRole(
        Guid organizationId, Guid userId, [FromForm] string role, CancellationToken cancellationToken)
    {
        var result = ModelState.IsValid
            ? await adminApi.ChangeMemberRoleAsync(organizationId, userId, role, cancellationToken)
            : ApiResult<bool>.Failure(Messages.RequestFailed);

        var message = string.Format(Messages.MemberRoleChanged, MembershipRoles.LabelOf(role));
        return await RedirectAfterMemberChangeAsync(result, message, organizationId, userId);
    }

    [HttpPost(OrganizationRoutes.RemoveMember)]
    public async Task<IActionResult> RemoveMember(Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        var result = await adminApi.RemoveMemberAsync(organizationId, userId, cancellationToken);
        return await RedirectAfterMemberChangeAsync(result, Messages.MemberRemoved, organizationId, userId);
    }

    // The form needs the organization's name. An organization the user is not an organization admin of goes
    // back to the list with the same message as the members page; the API still decides on the add itself
    private async Task<IActionResult> AddMemberViewAsync(
        Guid organizationId, AddMemberViewModel model, CancellationToken cancellationToken)
    {
        var organizations = await adminApi.ListMyOrganizationsAsync(cancellationToken);
        if (organizations.IsUnauthorized) return await SessionExpiredAsync();

        var organization = organizations.Value?.FirstOrDefault(o =>
            o.OrganizationId == organizationId && o.Role == MembershipRoles.OrgAdmin);
        if (organization is null)
        {
            TempData[TempDataKeys.Warning] = organizations.ErrorMessage ?? Messages.OrganizationNotFound;
            return RedirectToAction(nameof(Index));
        }

        model.OrganizationId = organizationId;
        model.OrganizationName = organization.OrganizationName;
        return View(nameof(AddMember), model);
    }

    private async Task<IActionResult> AddMemberFailedAsync<T>(
        Guid organizationId, ApiResult<T> result, AddMemberViewModel model, CancellationToken cancellationToken)
    {
        if (result.IsUnauthorized) return await SessionExpiredAsync();

        AddApiErrors(result);
        return await AddMemberViewAsync(organizationId, model, cancellationToken);
    }

    // After a change to their own membership the user may no longer manage the organization,
    // so a successful one goes back to their organizations instead of the members page
    private Task<IActionResult> RedirectAfterMemberChangeAsync(
        ApiResult<bool> result, string successMessage, Guid organizationId, Guid userId) =>
        result.IsSuccess && IsSignedInUser(userId)
            ? RedirectWithOutcomeAsync(result, successMessage, nameof(Index), ControllerNames.MyOrganizations)
            : RedirectWithOutcomeAsync(result, successMessage, nameof(Members), ControllerNames.MyOrganizations, new { organizationId });

    private bool IsSignedInUser(Guid userId) => SignedInUserId() == userId;

    // The user id from the API token's subject, kept in the session cookie
    private Guid? SignedInUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var signedInUserId) ? signedInUserId : null;
}

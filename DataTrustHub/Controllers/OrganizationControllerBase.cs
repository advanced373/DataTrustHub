using System.Security.Claims;
using DataTrustHub.Services.Api;
using DataTrustHub.Services.Session;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.Controllers;

// Shared pieces of the pages an organization admin manages one organization with.
// The API decides who may do what: it answers 404 Organization.NotFound outside the needed membership
public abstract class OrganizationControllerBase(IAdminApiClient adminApi, IUserSessionService userSession)
    : ApiControllerBase(userSession)
{
    protected IAdminApiClient AdminApi { get; } = adminApi;

    // The organization's name for a page heading, or null when it could not be loaded
    protected async Task<string?> OrganizationNameAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var organizations = await AdminApi.ListMyOrganizationsAsync(cancellationToken);
        return organizations.Value?
            .FirstOrDefault(organization => organization.OrganizationId == organizationId)?
            .OrganizationName;
    }

    // The signed-in user's membership in an organization they are an organization admin of. Any other
    // organization fails with the same message as the API's 404, so the forms are shown only to them
    protected async Task<ApiResult<MyOrganizationResponse>> AdministeredOrganizationAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var organizations = await AdminApi.ListMyOrganizationsAsync(cancellationToken);
        if (organizations.IsUnauthorized) return ApiResult<MyOrganizationResponse>.Unauthorized();

        var organization = organizations.Value?.FirstOrDefault(membership =>
            membership.OrganizationId == organizationId && membership.Role == MembershipRoles.OrgAdmin);
        return organization is null
            ? ApiResult<MyOrganizationResponse>.Failure(organizations.ErrorMessage ?? Messages.OrganizationNotFound)
            : ApiResult<MyOrganizationResponse>.Success(organization);
    }

    // A failed load, such as the API's 404 outside the organization, goes back to the organizations list
    protected Task<IActionResult> BackToMyOrganizationsAsync<T>(ApiResult<T> failure) =>
        RedirectWithOutcomeAsync(failure, string.Empty, nameof(MyOrganizationsController.Index), ControllerNames.MyOrganizations);

    // A refused form: the API's messages above the form, which is loaded again
    protected async Task<IActionResult> ShowFormWithApiErrorsAsync<T>(
        ApiResult<T> result, Func<Task<IActionResult>> showForm)
    {
        if (result.IsUnauthorized) return await SessionExpiredAsync();

        AddApiErrors(result);
        return await showForm();
    }

    // The user id from the API token's subject, kept in the session cookie
    protected Guid? SignedInUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var signedInUserId) ? signedInUserId : null;
}

using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.AppointOrganizationAdmin;

internal static class Constants
{
    internal const string EndpointRoute = "/admin/organizations/{organizationId:guid}/admins";
    internal const string EndpointTag = "Administration";
}

internal static class Errors
{
    internal static readonly Error OrganizationNotFound = Error.NotFound(
        "Organization.NotFound",
        "The organization was not found.");

    internal static readonly Error UserNotFound = Error.NotFound(
        "Membership.UserNotFound",
        "The specified user does not exist.");

    // The platform admin never appoints themselves, so they gain no role in the organization
    internal static readonly Error CannotAppointSelf = Error.Conflict(
        "Membership.CannotAppointSelf",
        "You cannot appoint yourself as organization admin.");

    // The platform admin appoints only the first organization admin; the others will be appointed by the
    // organization's own admins, by changing a member's role (step 2b of #95)
    internal static readonly Error AlreadyHasOrgAdmin = Error.Conflict(
        "Organization.AlreadyHasOrgAdmin",
        "The organization already has an organization admin.");

    // Only under concurrency: another request created the membership between the read and the save; a
    // retry gets AlreadyHasOrgAdmin
    internal static readonly Error AlreadyMember = Error.Conflict(
        "Membership.AlreadyMember",
        "The user is already a member of this organization.");
}

namespace DataTrustHub.Features.Tests.Administration.Helpers;

// The public error contract of the admin commands (codes and messages must not change)
internal static class AdminErrorCodes
{
    internal const string OrganizationNameAlreadyInUse = "Organization.NameAlreadyInUse";
    internal const string OrganizationNotFound = "Organization.NotFound";
    internal const string OrganizationHasPolicies = "Organization.HasPolicies";
    internal const string PolicyNameAlreadyInUse = "Policy.NameAlreadyInUse";
    internal const string PolicyNotFound = "Policy.NotFound";
    internal const string PolicyHasClearances = "Policy.HasClearances";
    internal const string AccountEmailAlreadyInUse = "Account.EmailAlreadyInUse";
    internal const string AccountNotFound = "Account.NotFound";
    internal const string AccountCannotDeleteOwnAccount = "Account.CannotDeleteOwnAccount";
    internal const string ClearanceUserNotFound = "Clearance.UserNotFound";
    internal const string ClearancePolicyNotFound = "Clearance.PolicyNotFound";
    internal const string ClearanceAlreadyGranted = "Clearance.AlreadyGranted";
    internal const string ClearanceNotFound = "Clearance.NotFound";
    internal const string MembershipUserNotFound = "Membership.UserNotFound";
    internal const string MembershipAlreadyMember = "Membership.AlreadyMember";
    internal const string MembershipGuestRoleIsFixed = "Membership.GuestRoleIsFixed";
    internal const string MembershipCannotAppointSelf = "Membership.CannotAppointSelf";
    internal const string OrganizationAlreadyHasOrgAdmin = "Organization.AlreadyHasOrgAdmin";
    internal const string MembershipNotFound = "Membership.NotFound";
    internal const string MembershipLastOrgAdmin = "Membership.LastOrgAdmin";
    internal const string MembershipUnknownRole = "Membership.UnknownRole";
    internal const string ValidationGeneral = "Validation.General";
}

internal static class AdminErrorMessages
{
    internal const string OrganizationNameAlreadyInUse = "An organization with this name already exists.";
    internal const string OrganizationNotFound = "The organization was not found.";
    internal const string OrganizationHasPolicies = "The organization still has policies. Delete its policies first.";
    internal const string PolicyNameAlreadyInUse = "A policy with this name already exists for the given organization.";
    internal const string PolicyNotFound = "The policy was not found.";
    internal const string PolicyHasClearances = "The policy still has clearances. Delete its clearances first.";
    internal const string AccountEmailAlreadyInUse = "A user with this email address already exists.";
    internal const string AccountNotFound = "The account was not found.";
    internal const string AccountCannotDeleteOwnAccount = "Administrators cannot delete their own account.";
    internal const string ClearanceUserNotFound = "The specified user does not exist.";
    internal const string ClearancePolicyNotFound = "The specified policy does not exist.";
    internal const string ClearanceAlreadyGranted = "The user already has a clearance for this policy.";
    internal const string ClearanceNotFound = "The clearance was not found.";
    internal const string MembershipUserNotFound = "The specified user does not exist.";
    internal const string MembershipAlreadyMember = "The user is already a member of this organization.";
    internal const string MembershipCannotAppointSelf = "You cannot appoint yourself as organization admin.";
    internal const string OrganizationAlreadyHasOrgAdmin = "The organization already has an organization admin.";
    internal const string MembershipGuestRoleIsFixed = "A guest stays a guest. End the membership and add the person again to change it.";
    internal const string MembershipNotFound = "The user is not a member of this organization.";
    internal const string MembershipLastOrgAdmin = "The organization must keep at least one organization admin. Appoint another one first.";
    internal const string ValidationGeneral = "One or more validation errors occurred";
}

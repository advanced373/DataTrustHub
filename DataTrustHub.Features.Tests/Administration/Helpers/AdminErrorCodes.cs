namespace DataTrustHub.Features.Tests.Administration.Helpers;

// The public error contract of the admin commands (codes and messages must not change)
internal static class AdminErrorCodes
{
    internal const string GroupNameAlreadyInUse = "Group.NameAlreadyInUse";
    internal const string GroupNotFound = "Group.NotFound";
    internal const string GroupHasPolicies = "Group.HasPolicies";
    internal const string PolicyOrganizationNotFound = "Policy.OrganizationNotFound";
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
}

internal static class AdminErrorMessages
{
    internal const string GroupNameAlreadyInUse = "A group with this name already exists.";
    internal const string GroupNotFound = "The group was not found.";
    internal const string GroupHasPolicies = "The group still has policies. Delete its policies first.";
    internal const string PolicyOrganizationNotFound = "The specified organization does not exist.";
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
}

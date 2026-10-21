namespace DataTrustHub.Features.Tests.Administration.Helpers;

// The public error contract of the group and policy commands (codes and messages must not change)
internal static class AdminErrorCodes
{
    internal const string GroupNameAlreadyInUse = "Group.NameAlreadyInUse";
    internal const string GroupNotFound = "Group.NotFound";
    internal const string GroupHasPolicies = "Group.HasPolicies";
    internal const string PolicyOrganizationNotFound = "Policy.OrganizationNotFound";
    internal const string PolicyNameAlreadyInUse = "Policy.NameAlreadyInUse";
    internal const string PolicyNotFound = "Policy.NotFound";
    internal const string PolicyHasClearances = "Policy.HasClearances";
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
}

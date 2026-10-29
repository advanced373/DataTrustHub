using System.ComponentModel.DataAnnotations;
using DataTrustHub.Services.Api;

namespace DataTrustHub.Models;

// The members page of one organization; the name is null when it could not be loaded.
// SignedInUserId marks the signed-in user's own row
public sealed record MembersViewModel(
    Guid OrganizationId,
    string? OrganizationName,
    IReadOnlyList<MemberResponse> Members,
    Guid? SignedInUserId);

// An organization admin adds a user, found by their email address, as a member or a guest
public sealed class AddMemberViewModel
{
    [Required, EmailAddress, StringLength(Constants.MaxEmailLength)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = MembershipRoles.Member;

    // Filled by the controller from the route and the user's organizations, not posted back
    public Guid OrganizationId { get; set; }

    public string OrganizationName { get; set; } = string.Empty;
}

// The tabs of an organization admin's pages; ActiveController marks the open tab
public sealed record OrganizationNavModel(Guid OrganizationId, string ActiveController);

// The policies page of one organization, sorted by name; the name is null when it could not be loaded
public sealed record PoliciesViewModel(
    Guid OrganizationId,
    string? OrganizationName,
    IReadOnlyList<PolicyResponse> Policies);

public sealed class CreatePolicyViewModel
{
    [Required, StringLength(Constants.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    // Filled by the controller from the route and the user's organizations, not posted back
    public Guid OrganizationId { get; set; }

    public string OrganizationName { get; set; } = string.Empty;
}

// A clearance in the list, with the grantee's email and the policy's name instead of their ids
public sealed record ClearanceRow(
    Guid Id, string MemberEmail, string PolicyName, string Name, string LevelName, int LevelPriority);

// The clearances page of one organization; the selected ids are the optional filters
public sealed record ClearancesViewModel(
    Guid OrganizationId,
    string? OrganizationName,
    IReadOnlyList<ClearanceRow> Clearances,
    IReadOnlyList<MemberResponse> Members,
    IReadOnlyList<PolicyResponse> Policies,
    Guid? SelectedUserId,
    Guid? SelectedPolicyId);

// An organization admin grants a member, guest or other organization admin a clearance on a policy
public sealed class GrantClearanceViewModel
{
    [Required(ErrorMessage = Messages.MemberRequired), Display(Name = "Member")]
    public Guid? UserId { get; set; }

    [Required(ErrorMessage = Messages.PolicyRequired), Display(Name = "Policy")]
    public Guid? PolicyId { get; set; }

    [Required(ErrorMessage = Messages.LevelRequired), Display(Name = "Classification level")]
    public int? LevelPriority { get; set; }

    // Optional; defaults to "<level> on <policy>"
    [StringLength(Constants.MaxNameLength), Display(Name = "Clearance name (optional)")]
    public string? Name { get; set; }

    // Filled by the controller, not posted back. Grantees leaves out the signed-in user (C8)
    public Guid OrganizationId { get; set; }

    public string OrganizationName { get; set; } = string.Empty;

    public IReadOnlyList<MemberResponse> Grantees { get; set; } = [];

    public IReadOnlyList<PolicyResponse> Policies { get; set; } = [];
}

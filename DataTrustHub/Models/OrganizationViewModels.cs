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

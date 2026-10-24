namespace DataTrustHub.Services.Api;

// Request and response shapes of the DataTrustHub API (DataTrustHub.Features endpoints)

internal sealed record CredentialsRequest(string Email, string Password);

internal sealed record CreateOrganizationRequest(string Name);

internal sealed record FindUserByEmailRequest(string Email);

// Role is Member or Guest
internal sealed record AddMemberRequest(Guid UserId, string Role);

// Role is Member or OrgAdmin
internal sealed record ChangeMemberRoleRequest(string Role);

internal sealed record AppointOrganizationAdminRequest(Guid UserId);

internal sealed record LoginResponse(string Token);

internal sealed record UserCreatedResponse(Guid UserId);

internal sealed record DataItemCreatedResponse(Guid DataItemId);

// A data item the user may read; policy fields are null for a personal item. CanDelete says whether the
// user may delete it (D5'), IsOwner whether they uploaded it
public sealed record DataItemResponse(
    Guid Id,
    string Name,
    long Size,
    string SecurityMarking,
    int? ClassificationPriority,
    Guid? PolicyId,
    string? PolicyName,
    string? OwnerEmail,
    bool IsOwner,
    bool CanDelete);

// What the user may classify an upload as: all levels, and the policies they are cleared on
public sealed record ClassificationOptionsResponse(
    IReadOnlyList<ClassificationLevelOption> Levels,
    IReadOnlyList<ClassificationPolicyOption> Policies);

public sealed record ClassificationLevelOption(string Name, int Priority);

// MaxPriority is the user's highest clearance level on the policy
public sealed record ClassificationPolicyOption(Guid PolicyId, string PolicyName, string OrganizationName, int MaxPriority);

// One item with its content, base64-encoded by the API
public sealed record DataItemContentResponse(Guid Id, string Name, string? Content);

internal sealed record OrganizationCreatedResponse(Guid OrganizationId);

internal sealed record MembershipCreatedResponse(Guid MembershipId);

public sealed record AccountResponse(Guid Id, string Email, string Role);

public sealed record OrganizationResponse(Guid Id, string Name);

// One of the signed-in user's memberships, with the organization it is in
public sealed record MyOrganizationResponse(Guid OrganizationId, string OrganizationName, Guid MembershipId, string Role);

public sealed record MemberResponse(Guid MembershipId, Guid UserId, string Email, string Role);

// Role is the user's membership role in the organization, or null when they are not in it
public sealed record UserByEmailResponse(Guid UserId, string Email, bool IsMember, string? Role);

// Covers SharedKernel.Error (description), ValidationError (errors) and ProblemDetails (detail) bodies
internal sealed record ApiError(string? Code, string? Description, string? Detail, ApiError[]? Errors);

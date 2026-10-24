namespace DataTrustHub.Services.Api;

// Request and response shapes of the DataTrustHub API (DataTrustHub.Features endpoints)

internal sealed record CredentialsRequest(string Email, string Password);

internal sealed record CreatePolicyRequest(string Name, Guid OrganizationId);

internal sealed record CreateOrganizationRequest(string Name);

internal sealed record CreateClearanceRequest(
    Guid UserId, Guid PolicyId, string Name, string ClassificationLevelName, int ClassificationLevelPriority);

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

internal sealed record PolicyCreatedResponse(Guid PolicyId);

internal sealed record OrganizationCreatedResponse(Guid OrganizationId);

internal sealed record ClearanceCreatedResponse(Guid ClearanceId);

public sealed record AccountResponse(Guid Id, string Email, string Role);

public sealed record OrganizationResponse(Guid Id, string Name);

public sealed record PolicyResponse(Guid Id, string Name, Guid OrganizationId);

public sealed record ClearanceResponse(
    Guid Id, string Name, Guid UserId, Guid PolicyId, string ClassificationLevelName, int ClassificationLevelPriority);

// What the admin fills in to grant a clearance
public sealed record ClearanceGrant(Guid UserId, Guid PolicyId, string Name, string LevelName, int LevelPriority);

// Covers SharedKernel.Error (description), ValidationError (errors) and ProblemDetails (detail) bodies
internal sealed record ApiError(string? Code, string? Description, string? Detail, ApiError[]? Errors);

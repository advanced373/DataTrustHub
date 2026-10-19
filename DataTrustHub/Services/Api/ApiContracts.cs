namespace DataTrustHub.Services.Api;

// Request and response shapes of the DataTrustHub API (DataTrustHub.Features endpoints)

internal sealed record CredentialsRequest(string Email, string Password);

internal sealed record CreatePolicyRequest(string Name, Guid OrganizationId);

internal sealed record CreateGroupRequest(string Name);

internal sealed record LoginResponse(string Token);

internal sealed record UserCreatedResponse(Guid UserId);

internal sealed record DataItemCreatedResponse(Guid DataItemId);

internal sealed record PolicyCreatedResponse(Guid PolicyId);

internal sealed record GroupCreatedResponse(Guid GroupId);

// Covers SharedKernel.Error (description), ValidationError (errors) and ProblemDetails (detail) bodies
internal sealed record ApiError(string? Code, string? Description, string? Detail, ApiError[]? Errors);

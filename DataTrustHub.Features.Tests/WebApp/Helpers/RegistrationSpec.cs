using System.Text.Json;

namespace DataTrustHub.Features.Tests.WebApp.Helpers;

// The texts and API bodies issue #172 specifies for the WebApp's register page and registration requests page
internal static class RegistrationSpec
{
    internal const string RequestSent =
        "Your registration request was sent. You can sign in once an administrator approves it.";
    internal const string TooManyRegistrationAttempts = "Too many registration attempts. Try again later.";
    internal const string EmptyState = "No registration requests are waiting.";
    internal const string NavEntry = "Registration requests";
    internal const string ApprovedFormat = "Registration of {0} approved; the account was created.";
    internal const string RejectedFormat = "Registration of {0} rejected.";

    internal const string ApproveAction = "ApproveRegistrationRequest";
    internal const string RejectAction = "RejectRegistrationRequest";

    // Times are shown in UTC like the audit trail
    internal const string TimeFormat = "yyyy-MM-dd HH:mm:ss 'UTC'";

    // The API's error codes and descriptions (the WebApp shows the description)
    internal const string EmailInUseCode = "User.EmailAlreadyInUse";
    internal const string EmailInUseDescription = "An account with this email address already exists.";
    internal const string AlreadyPendingCode = "Registration.AlreadyPending";
    internal const string AlreadyPendingDescription = "A registration request for this email address is already waiting.";
    internal const string QueueFullCode = "Registration.QueueFull";
    internal const string QueueFullDescription = "Too many registrations are waiting for approval. Try again later.";
    internal const string NotFoundCode = "RegistrationRequest.NotFound";
    internal const string NotFoundDescription = "The registration request was not found.";

    internal static string ErrorBody(string code, string description) =>
        JsonSerializer.Serialize(new { code, description });

    internal static string RegistrationRequestCreatedBody(Guid id) =>
        JsonSerializer.Serialize(new { registrationRequestId = id });

    internal static string UserCreatedBody(Guid userId) => JsonSerializer.Serialize(new { userId });

    internal static string RequestsBody(params PendingRequest[] requests) =>
        JsonSerializer.Serialize(requests.Select(request => new
        {
            id = request.Id,
            email = request.Email,
            requestedAt = request.RequestedAt,
            expiresAt = request.ExpiresAt
        }));
}

internal sealed record PendingRequest(Guid Id, string Email, DateTime RequestedAt, DateTime ExpiresAt);

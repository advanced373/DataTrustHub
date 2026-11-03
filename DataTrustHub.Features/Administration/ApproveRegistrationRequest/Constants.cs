using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.ApproveRegistrationRequest;

internal static class Constants
{
    internal const string EndpointRoute = "/admin/registration-requests/{id:guid}/approve";
    internal const string EndpointTag = "Administration";
}

internal static class Errors
{
    // U1: the email got an account after the request was made; the request stays, for the admin to reject
    internal static readonly Error EmailAlreadyInUse = Error.Conflict(
        "User.EmailAlreadyInUse",
        "A user with this email address already exists.");
}

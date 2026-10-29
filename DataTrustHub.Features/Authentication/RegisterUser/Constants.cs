using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Authentication.RegisterUser;

internal static class Constants
{
    internal const string EndpointRoute = "/auth/register";
    internal const string EndpointTag = "Authentication";
    internal const int MinPasswordLength = 8;
    internal const int MaxEmailLength = 256;
}

internal static class Errors
{
    internal static readonly Error EmailAlreadyInUse = Error.Conflict(
        "User.EmailAlreadyInUse",
        "A user with this email address already exists.");
}

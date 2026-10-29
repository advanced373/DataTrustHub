using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Authentication.LoginUser;

internal static class Constants
{
    internal const string EndpointRoute = "/auth/login";
    internal const string EndpointTag = "Authentication";
}

internal static class Errors
{
    internal static readonly Error InvalidCredentials = Error.Problem(
        "User.InvalidCredentials",
        "Invalid email or password.");
}

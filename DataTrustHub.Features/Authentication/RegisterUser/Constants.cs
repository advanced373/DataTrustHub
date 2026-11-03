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

    // R1: one pending registration request per email address
    internal static readonly Error RegistrationAlreadyPending = Error.Conflict(
        "Registration.AlreadyPending",
        "A registration request for this email address is already waiting for approval.");

    // R3: the queue of pending registration requests is full; the client gets 503
    internal static readonly Error RegistrationQueueFull = Error.Unavailable(
        "Registration.QueueFull",
        "Registrations are temporarily closed. Try again later.");
}

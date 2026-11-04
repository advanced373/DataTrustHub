namespace DataTrustHub.Features.Tests.Authentication.Helpers;

// The public error contract of register and login (codes and messages must not change)
internal static class AuthErrorCodes
{
    internal const string EmailAlreadyInUse = "User.EmailAlreadyInUse";
    internal const string InvalidCredentials = "User.InvalidCredentials";
    internal const string RegistrationAlreadyPending = "Registration.AlreadyPending";
    internal const string RegistrationQueueFull = "Registration.QueueFull";
}

internal static class AuthErrorMessages
{
    internal const string EmailAlreadyInUse = "A user with this email address already exists.";
    internal const string InvalidCredentials = "Invalid email or password.";
    internal const string RegistrationAlreadyPending =
        "A registration request for this email address is already waiting for approval.";
    internal const string RegistrationQueueFull = "Registrations are temporarily closed. Try again later.";
}

using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Registrations;

public static class RegistrationRequestErrors
{
    // Also the answer for an expired request: it no longer counts (R2) and is left for the purge
    public static readonly Error NotFound = Error.NotFound(
        "RegistrationRequest.NotFound",
        "The registration request was not found.");
}

using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Shares;

public static class ShareErrors
{
    // Also the answer for a share already revoked: an ended share looks like a missing one
    public static readonly Error NotFound = Error.NotFound(
        "Share.NotFound",
        "The share was not found.");

    // Invariant S4; a validation error, so the client gets 400 with the reason
    public static readonly Error ExpiryNotAfterSharedAt = new(
        "Share.ExpiryNotAfterSharedAt",
        "A share's expiry date must be later than the moment it is shared.",
        ErrorType.Validation);
}

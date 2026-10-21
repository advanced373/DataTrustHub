using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Users;

public static class UserErrors
{
    public static readonly Error CannotDeleteOwnAccount = Error.Conflict(
        "Account.CannotDeleteOwnAccount",
        "Administrators cannot delete their own account.");
}

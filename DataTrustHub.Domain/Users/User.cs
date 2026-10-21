using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Users;

public sealed class User
{
    // Used by the persistence mapper; new users are created through Register or CreateAdmin
    private User() { }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string HashedPassword { get; private set; } = string.Empty;

    public string Role { get; private set; } = UserRoles.User;

    public bool IsAdmin => Role == UserRoles.Admin;

    public static User Register(string email, string hashedPassword) =>
        new() { Id = Guid.NewGuid(), Email = email, HashedPassword = hashedPassword, Role = UserRoles.User };

    public static User CreateAdmin(string email, string hashedPassword) =>
        new() { Id = Guid.NewGuid(), Email = email, HashedPassword = hashedPassword, Role = UserRoles.Admin };

    // Returns false when the user already was an admin
    public bool PromoteToAdmin()
    {
        if (IsAdmin) return false;

        Role = UserRoles.Admin;
        return true;
    }

    // An administrator deleting their own account could lock everyone out
    public Result CheckCanBeDeletedBy(Guid requesterId) =>
        requesterId == Id ? Result.Failure(UserErrors.CannotDeleteOwnAccount) : Result.Success();
}

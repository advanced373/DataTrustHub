using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Users;

public sealed class User
{
    // Rebuilds a stored entity; used by the persistence mappers only (internal, visible to Infrastructure)
    internal User(Guid id, string email, string hashedPassword, string role)
    {
        Id = id;
        Email = email;
        HashedPassword = hashedPassword;
        Role = role;
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; }

    public string HashedPassword { get; private set; }

    public string Role { get; private set; }

    public bool IsAdmin => Role == UserRoles.Admin;

    // The factory methods store the normalized address (U7); rehydration keeps what is stored
    public static User Register(string email, string hashedPassword) =>
        new(Guid.NewGuid(), EmailAddress.From(email).Value, hashedPassword, UserRoles.User);

    public static User CreateAdmin(string email, string hashedPassword) =>
        new(Guid.NewGuid(), EmailAddress.From(email).Value, hashedPassword, UserRoles.Admin);

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

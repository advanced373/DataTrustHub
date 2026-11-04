using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Users;

public sealed class User
{
    private const string HashedPasswordRequired = "A user's password hash cannot be empty.";

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

    // Replaces the stored password hash with a newer one, when sign-in re-hashes a password stored in an
    // older format (U8). An empty hash is a programming error, not a business outcome
    public void ReplacePasswordHash(string hashedPassword)
    {
        if (string.IsNullOrEmpty(hashedPassword))
            throw new ArgumentException(HashedPasswordRequired, nameof(hashedPassword));

        HashedPassword = hashedPassword;
    }

    // An administrator deleting their own account could lock everyone out
    public Result CheckCanBeDeletedBy(Guid requesterId) =>
        requesterId == Id ? Result.Failure(UserErrors.CannotDeleteOwnAccount) : Result.Success();
}

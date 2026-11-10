namespace DataTrustHub.Domain.ReadModels.Users;

// The read side of users and their accounts (ADR-0012 step 4): every method projects straight to a read
// model in SQL, and none of them carries the password hash. No authorization here: the caller checks who
// may read first
public interface IUserQueries
{
    // Every account of the platform, ordered by email
    Task<IReadOnlyList<AccountEntry>> ListAccountsAsync(CancellationToken cancellationToken);

    // The account, or null when no user has this id
    Task<AccountEntry?> FindAccountAsync(Guid userId, CancellationToken cancellationToken);

    // The user whose email equals the given address, or null. The caller passes the normalized address (U7):
    // the match is exact, by equality in the database
    Task<UserByEmail?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
}

using DataTrustHub.Domain.ReadModels.Users;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.ReadModels;

// Projects straight to the read models in SQL, untracked; the password hash is never selected
internal sealed class UserQueries(DContext db) : IUserQueries
{
    public async Task<IReadOnlyList<AccountEntry>> ListAccountsAsync(CancellationToken cancellationToken) =>
        await db.Users
            .AsNoTracking()
            .OrderBy(user => user.Email)
            .Select(user => new AccountEntry(user.Id, user.Email, user.Role))
            .ToListAsync(cancellationToken);

    public Task<AccountEntry?> FindAccountAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new AccountEntry(user.Id, user.Email, user.Role))
            .FirstOrDefaultAsync(cancellationToken);

    // The same comparison as log in (IUserRepository.GetByEmailAsync): equality with the normalized address
    public Task<UserByEmail?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        db.Users
            .AsNoTracking()
            .Where(user => user.Email == normalizedEmail)
            .Select(user => new UserByEmail(user.Id, user.Email))
            .FirstOrDefaultAsync(cancellationToken);
}

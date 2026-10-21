using DataTrustHub.Domain.Users;
using DataTrustHub.Infrastructure.Persistance.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.Repositories;

// Reads return detached domain entities; writes are tracked here and committed by IUnitOfWork
internal sealed class UserRepository(DContext db) : IUserRepository
{
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken))?.ToDomain();

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
        (await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, cancellationToken))?.ToDomain();

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Id == id, cancellationToken);

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Email == email, cancellationToken);

    public void Add(User user) => db.Users.Add(user.ToDb());

    public async Task UpdateAsync(User user, CancellationToken cancellationToken)
    {
        var row = await db.Users.FirstAsync(u => u.Id == user.Id, cancellationToken);
        user.CopyTo(row);
    }

    public async Task RemoveAsync(User user, CancellationToken cancellationToken) =>
        db.Users.Remove(await db.Users.FirstAsync(u => u.Id == user.Id, cancellationToken));
}

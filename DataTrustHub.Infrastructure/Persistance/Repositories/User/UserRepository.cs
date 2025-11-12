using DataTrustHub.Domain.User;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using DomainUser = DataTrustHub.Domain.User;

namespace DataTrustHub.Infrastructure.Persistance.Repositories.User
{
    public class UserRepository(DContext context) : IUserRepository
    {
        private readonly DContext _context = context;

        public async Task<List<DomainUser.User>> GetAllAsync() =>
            await _context.Users.Select(u => new DomainUser.User
            {
                Id = u.Id,
                Email = u.Email,
                HashedPassword = u.HashedPassword
            }).ToListAsync();

        public async Task<DomainUser.User?> GetByIdAsync(Guid id)
        {
            var dbUser = await _context.Users.FindAsync(id);
            if (dbUser == null) return null;

            return new DomainUser.User
            {
                Id = dbUser.Id,
                Email = dbUser.Email,
                HashedPassword = dbUser.HashedPassword
            };
        }

        public async Task AddAsync(DomainUser.User user)
        {
            var dbUser = new DbUser
            {
                Id = user.Id,
                Email = user.Email,
                HashedPassword = user.HashedPassword
            };
            await _context.Users.AddAsync(dbUser);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(DomainUser.User user)
        {
            var dbUser = new DbUser 
            {
                Id = user.Id,
                Email = user.Email,
                HashedPassword = user.HashedPassword
            };
            _context.Users.Update(dbUser);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            var dbUser = await _context.Users.FindAsync(id);
            if (dbUser != null)
            {
                _context.Users.Remove(dbUser);
                await _context.SaveChangesAsync();
            }
        }
    }
}

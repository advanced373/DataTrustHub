using DataTrustHub.Domain.User;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Infrastructure.Persistance.Repositories.User
{
    public class UserRepository : IUserRepository
    {
        private readonly DContext _context;

        public UserRepository(DContext context)
        {
            _context = context;
        }

        public Task<Result<bool>> CreateUserAsync(string userId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<Result<bool>> DeleteUserAsync(string userId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}

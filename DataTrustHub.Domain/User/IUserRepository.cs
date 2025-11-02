using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.User
{
    public interface IUserRepository
    {
        public Task<Result<bool>> CreateUserAsync(string userId, CancellationToken cancellationToken = default);
        public Task<Result<bool>> DeleteUserAsync(string userId, CancellationToken cancellationToken = default);
    }
}

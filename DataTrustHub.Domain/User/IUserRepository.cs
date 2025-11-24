
namespace DataTrustHub.Domain.User
{
    public interface IUserRepository
    {
        public Task<List<User>> GetAllAsync();

        public Task<User?> GetByIdAsync(Guid id);

        public Task AddAsync(User user);

        public Task UpdateAsync(User user);

        public Task DeleteAsync(Guid id);
    }
}

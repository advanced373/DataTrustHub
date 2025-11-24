
namespace DataTrustHub.Domain.Policy
{
    public interface IPolicyRepository
    {
        public Task<List<Policy>> GetAllAsync();

        public Task<Policy?> GetByIdAsync(Guid id);

        public Task AddAsync(Policy policy);

        public Task UpdateAsync(Policy policy);

        public Task DeleteAsync(Guid id);
    }
}

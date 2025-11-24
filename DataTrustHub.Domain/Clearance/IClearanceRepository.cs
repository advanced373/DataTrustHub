
namespace DataTrustHub.Domain.Clearance
{
    public interface IClearanceRepository
    {
        public Task<List<Clearance>> GetAllAsync();

        public Task<Clearance?> GetByIdAsync(Guid id);

        public Task AddAsync(Clearance clearance);

        public Task UpdateAsync(Clearance clearance);

        public Task DeleteAsync(Guid id);
    }
}

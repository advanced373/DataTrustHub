
namespace DataTrustHub.Domain.Organization
{
    public interface IOrganizationRepository
    {
        public Task<List<Organization>> GetAllAsync();

        public Task<Organization?> GetByIdAsync(Guid id);

        public Task AddAsync(Organization organization);

        public Task UpdateAsync(Organization organization);

        public Task DeleteAsync(Guid id);
    }
}

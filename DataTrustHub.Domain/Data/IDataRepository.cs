
namespace DataTrustHub.Domain.Data
{
    public interface IDataItemRepository
    {
        public Task<List<DataItem>> GetAllAsync();

        public Task<DataItem?> GetByIdAsync(Guid id);

        public Task AddAsync(DataItem dataItem);

        public Task UpdateAsync(DataItem dataItem);

        public Task DeleteAsync(Guid id);
    }
}

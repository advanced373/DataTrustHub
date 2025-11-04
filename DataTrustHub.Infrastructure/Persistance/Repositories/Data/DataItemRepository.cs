using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using DomainData = DataTrustHub.Domain.Data;

namespace DataTrustHub.Infrastructure.Persistance.Repositories.Data
{
    public class DataItemRepository : DomainData.IDataRepository
    {
        private readonly DContext _context;
        public DataItemRepository(DContext context) => _context = context;

        public async Task<List<DomainData.DataItem>> GetAllAsync() =>
            await _context.DataItems.Select(d => new DomainData.DataItem
            {
                Id = d.Id,
                Name = d.Name,
                Size = d.Size,
                Content = d.Content,
                OwnerUserId = d.OwnerUserId,
                SecurityMarking = d.SecurityMarking
            }).ToListAsync();

        public async Task<DomainData.DataItem?> GetByIdAsync(Guid id)
        {
            var dbDataItem = await _context.DataItems.FindAsync(id);
            if (dbDataItem == null) return null;
            
            return new DomainData.DataItem
            {
                Id = dbDataItem.Id,
                Name = dbDataItem.Name,
                Size = dbDataItem.Size,
                Content = dbDataItem.Content,
                OwnerUserId = dbDataItem.OwnerUserId,
                SecurityMarking = dbDataItem.SecurityMarking
            };
        }

        public async Task AddAsync(DomainData.DataItem dataItem)
        {
            var dbDataItem = new DbDataItem
            {
                Id = dataItem.Id,
                Name = dataItem.Name,
                Size = dataItem.Size,
                Content = dataItem.Content,
                OwnerUserId = dataItem.OwnerUserId,
                SecurityMarking = dataItem.SecurityMarking
            };
            await _context.DataItems.AddAsync(dbDataItem);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(DomainData.DataItem dataItem)
        {
            var dbDataItem = new DbDataItem
            {
                Id = dataItem.Id,
                Name = dataItem.Name,
                Size = dataItem.Size,
                Content = dataItem.Content,
                OwnerUserId = dataItem.OwnerUserId,
                SecurityMarking = dataItem.SecurityMarking
            };
            _context.DataItems.Update(dbDataItem);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            var dbEntity = await _context.DataItems.FindAsync(id);
            if (dbEntity != null)
            {
                _context.DataItems.Remove(dbEntity);
                await _context.SaveChangesAsync();
            }
        }
    }
}

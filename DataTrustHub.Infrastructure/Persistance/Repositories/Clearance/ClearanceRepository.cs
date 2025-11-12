using DataTrustHub.Domain.ClassificationLevel;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using DomainClearance = DataTrustHub.Domain.Clearance;

namespace DataTrustHub.Infrastructure.Persistance.Repositories.Clearance
{
    public class ClearanceRepository(DContext context) : DomainClearance.IClearanceRepository
    {
        private readonly DContext _context = context;

        public async Task<List<DomainClearance.Clearance>> GetAllAsync() =>
            await _context.Clearances.Select(d => new DomainClearance.Clearance
            {
                Id = d.Id,
                Name = d.Name,
                UserId = d.UserId,
                PolicyId = d.PolicyId,
                ClassificationLevel = new ClassificationLevel() { Id = d.ClassificationLevel.Id, Name = d.ClassificationLevel.Name, Priority = d.ClassificationLevel.Priority }
            }).ToListAsync();

        public async Task<DomainClearance.Clearance?> GetByIdAsync(Guid id)
        {
            var dbClearance = await _context.Clearances.FindAsync(id);
            if (dbClearance == null) return null;

            return new DomainClearance.Clearance
            {
                Id = dbClearance.Id,
                Name = dbClearance.Name,
                UserId = dbClearance.UserId,
                PolicyId = dbClearance.PolicyId,
                ClassificationLevel = new ClassificationLevel() { Id = dbClearance.ClassificationLevel.Id, Name = dbClearance.ClassificationLevel.Name, Priority = dbClearance.ClassificationLevel.Priority }
            };
        }

        public async Task AddAsync(DomainClearance.Clearance clearance)
        {
            var dbClearance = new DbClearance
            {
                Id = clearance.Id,
                Name = clearance.Name,
                UserId = clearance.UserId,
                PolicyId = clearance.PolicyId,
                ClassificationLevel = new DbClassificationLevel() { Name = clearance.ClassificationLevel.Name, Priority = clearance.ClassificationLevel.Priority }
            };
            await _context.Clearances.AddAsync(dbClearance);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(DomainClearance.Clearance clearance)
        {
            var dbClearance = new DbClearance
            {
                Id = clearance.Id,
                Name = clearance.Name,
                UserId = clearance.UserId,
                PolicyId = clearance.PolicyId,
                ClassificationLevel = new DbClassificationLevel() { Name = clearance.ClassificationLevel.Name, Priority = clearance.ClassificationLevel.Priority }
            };
            _context.Clearances.Update(dbClearance);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            var dbEntity = await _context.Clearances.FindAsync(id);
            if (dbEntity != null)
            {
                _context.Clearances.Remove(dbEntity);
                await _context.SaveChangesAsync();
            }
        }
    }
}

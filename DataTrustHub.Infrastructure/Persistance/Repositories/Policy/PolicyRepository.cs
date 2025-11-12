using DataTrustHub.Domain.ClassificationLevel;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using DomainPolicy = DataTrustHub.Domain.Policy;

namespace DataTrustHub.Infrastructure.Persistance.Repositories.Policy
{
    public class PolicyRepository(DContext context) : DomainPolicy.IPolicyRepository
    {
        private readonly DContext _context = context;

        public async Task<List<DomainPolicy.Policy>> GetAllAsync() =>
            await _context.Policies.Select(p => new DomainPolicy.Policy
            {
                Id = p.Id,
                Name = p.Name,
                OrganizationId = p.OrganizationId,
                ClassificationLevels = p.ClassificationLevels.Select(cl => new ClassificationLevel
                {
                    Id = cl.Id,
                    Name = cl.Name,
                    Priority = cl.Priority
                }).ToList()
            }).ToListAsync();

        public async Task<DomainPolicy.Policy?> GetByIdAsync(Guid id)
        {
            var dbPolicy = await _context.Policies.FindAsync(id);
            if (dbPolicy == null) return null;
            
            return new DomainPolicy.Policy
            {
                Id = dbPolicy.Id,
                Name = dbPolicy.Name,
                OrganizationId = dbPolicy.OrganizationId,
                ClassificationLevels = dbPolicy.ClassificationLevels.Select(cl => new ClassificationLevel
                {
                    Id = cl.Id,
                    Name = cl.Name,
                    Priority = cl.Priority
                }).ToList()
            };
        }

        public async Task AddAsync(DomainPolicy.Policy policy)
        {
            var dbPolicy = new DbPolicy
            {
                Id = policy.Id,
                Name = policy.Name,
                OrganizationId = policy.OrganizationId,
                ClassificationLevels = (DbSet<DbClassificationLevel>)policy.ClassificationLevels.Select(cl => new DbClassificationLevel
                {
                    Name = cl.Name,
                    Priority = cl.Priority
                })
            };
            await _context.Policies.AddAsync(dbPolicy);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(DomainPolicy.Policy policy)
        {
            var dbPolicy = new DbPolicy
            {
                Id = policy.Id,
                Name = policy.Name,
                OrganizationId = policy.OrganizationId,
                ClassificationLevels = (DbSet<DbClassificationLevel>)policy.ClassificationLevels.Select(cl => new DbClassificationLevel
                {
                    Name = cl.Name,
                    Priority = cl.Priority
                })
            };
            _context.Policies.Update(dbPolicy);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            var dbEntity = await _context.Policies.FindAsync(id);
            if (dbEntity != null)
            {
                _context.Policies.Remove(dbEntity);
                await _context.SaveChangesAsync();
            }
        }
    }
}

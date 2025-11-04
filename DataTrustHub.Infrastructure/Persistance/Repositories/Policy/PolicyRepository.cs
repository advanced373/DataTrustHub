using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using DomainPolicy = DataTrustHub.Domain.Policy;

namespace DataTrustHub.Infrastructure.Persistance.Repositories.Policy
{
    public class PolicyRepository : DomainPolicy.IPolicyRepository
    {
        private readonly DContext _context;
        public PolicyRepository(DContext context) => _context = context;

        public async Task<List<DomainPolicy.Policy>> GetAllAsync() =>
            await _context.Policies.Select(p => new DomainPolicy.Policy
            {
                Id = p.Id,
                Name = p.Name,
                OrganizationId = p.OrganizationId
            }).ToListAsync();

        public async Task<DomainPolicy.Policy?> GetByIdAsync(Guid id)
        {
            var dbPolicy = await _context.Policies.FindAsync(id);
            if (dbPolicy == null) return null;
            
            return new DomainPolicy.Policy
            {
                Id = dbPolicy.Id,
                Name = dbPolicy.Name,
                OrganizationId = dbPolicy.OrganizationId
            };
        }

        public async Task AddAsync(DomainPolicy.Policy policy)
        {
            var dbPolicy = new DbPolicy
            {
                Id = policy.Id,
                Name = policy.Name,
                OrganizationId = policy.OrganizationId
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
                OrganizationId = policy.OrganizationId
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

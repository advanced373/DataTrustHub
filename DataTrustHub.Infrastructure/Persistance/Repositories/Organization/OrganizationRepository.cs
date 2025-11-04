using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using DomainOrganization = DataTrustHub.Domain.Organization;

namespace DataTrustHub.Infrastructure.Persistance.Repositories.Organization
{
    public class OrganizationRepository : DomainOrganization.IOrganizationRepository
    {
        private readonly DContext _context;
        public OrganizationRepository(DContext context) => _context = context;

        public async Task<List<DomainOrganization.Organization>> GetAllAsync() =>
            await _context.Organizations.Select(o => new DomainOrganization.Organization
            {
                Id = o.Id,
                Name = o.Name
            }).ToListAsync();

        public async Task<DomainOrganization.Organization?> GetByIdAsync(Guid id)
        {
            var dbOrg = await _context.Organizations.FindAsync(id);
            if (dbOrg == null) return null;
            
            return new DomainOrganization.Organization
            {
                Id = dbOrg.Id,
                Name = dbOrg.Name
            };
        }

        public async Task AddAsync(DomainOrganization.Organization organization)
        {
            var dbOrg = new DbOrganization
            {
                Id = organization.Id,
                Name = organization.Name
            };
            await _context.Organizations.AddAsync(dbOrg);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(DomainOrganization.Organization organization)
        {
            var dbOrg = new DbOrganization
            {
                Id = organization.Id,
                Name = organization.Name
            };
            _context.Organizations.Update(dbOrg);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            var dbEntity = await _context.Organizations.FindAsync(id);
            if (dbEntity != null)
            {
                _context.Organizations.Remove(dbEntity);
                await _context.SaveChangesAsync();
            }
        }
    }
}

using DataTrustHub.Domain.Audit;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using DomainAudit = DataTrustHub.Domain.Audit;

namespace DataTrustHub.Infrastructure.Persistance.Repositories.Audit
{
    public class AuditRepository(DContext context) : IAuditRepository
    {
        private readonly DContext _context = context;

        public async Task AddAsync(DomainAudit.AuditLog auditLog)
        {
            var dbAudit = new DbAudit
            {
                Id = auditLog.Id,
                Action = auditLog.Action,
                EntityType = auditLog.EntityType,
                EntityId = auditLog.EntityId,
                Timestamp = auditLog.Timestamp,
                UserId = auditLog.UserId,
                IpAddress = auditLog.IpAddress,
                UserAgent = auditLog.UserAgent,
                RequestPath = auditLog.RequestPath,
                RequestMethod = auditLog.RequestMethod
            };
            await _context.Audits.AddAsync(dbAudit);
            await _context.SaveChangesAsync();
        }

        public async Task<List<DomainAudit.AuditLog>> GetAllAsync()
        {
            return await _context.Audits.Select(a => new DomainAudit.AuditLog
            {
                Id = a.Id,
                Action = a.Action,
                EntityType = a.EntityType,
                EntityId = a.EntityId,
                Timestamp = a.Timestamp,
                UserId = a.UserId,
                IpAddress = a.IpAddress,
                UserAgent = a.UserAgent,
                RequestPath = a.RequestPath,
                RequestMethod = a.RequestMethod
            }).ToListAsync();
        }

        public async Task<List<DomainAudit.AuditLog>> GetByEntityTypeAsync(string entityType)
        {
            return await _context.Audits
                .Where(a => a.EntityType == entityType)
                .Select(a => new DomainAudit.AuditLog
                {
                    Id = a.Id,
                    Action = a.Action,
                    EntityType = a.EntityType,
                    EntityId = a.EntityId,
                    Timestamp = a.Timestamp,
                    UserId = a.UserId,
                    IpAddress = a.IpAddress,
                    UserAgent = a.UserAgent,
                    RequestPath = a.RequestPath,
                    RequestMethod = a.RequestMethod
                }).ToListAsync();
        }

        public async Task<List<DomainAudit.AuditLog>> GetByEntityIdAsync(Guid entityId)
        {
            return await _context.Audits
                .Where(a => a.EntityId == entityId)
                .Select(a => new DomainAudit.AuditLog
                {
                    Id = a.Id,
                    Action = a.Action,
                    EntityType = a.EntityType,
                    EntityId = a.EntityId,
                    Timestamp = a.Timestamp,
                    UserId = a.UserId,
                    IpAddress = a.IpAddress,
                    UserAgent = a.UserAgent,
                    RequestPath = a.RequestPath,
                    RequestMethod = a.RequestMethod
                }).ToListAsync();
        }
    }
}


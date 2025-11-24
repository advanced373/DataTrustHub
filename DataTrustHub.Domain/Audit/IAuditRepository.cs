namespace DataTrustHub.Domain.Audit
{
    public interface IAuditRepository
    {
        Task AddAsync(AuditLog auditLog);
        Task<List<AuditLog>> GetAllAsync();
        Task<List<AuditLog>> GetByEntityTypeAsync(string entityType);
        Task<List<AuditLog>> GetByEntityIdAsync(Guid entityId);
    }
}


using DataTrustHub.Domain.ReadModels.Auditing;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.ReadModels;

// Projects straight to the read model in SQL, untracked; both trails share the filters, the left join, the
// order and the cut, and differ only in their scope
internal sealed class AuditRecordQueries(DContext db) : IAuditRecordQueries
{
    public Task<IReadOnlyList<AuditRecordEntry>> ListForActionsAsync(
        IReadOnlyCollection<string> actions,
        AuditRecordFilter filter,
        int maxRecords,
        CancellationToken cancellationToken) =>
        ListAsync(
            db.AuditRecords.AsNoTracking().Where(record => actions.Contains(record.Action)),
            filter,
            maxRecords,
            cancellationToken);

    public Task<IReadOnlyList<AuditRecordEntry>> ListForOrganizationAsync(
        Guid organizationId,
        AuditRecordFilter filter,
        int maxRecords,
        CancellationToken cancellationToken) =>
        ListAsync(
            db.AuditRecords.AsNoTracking().Where(record => record.OrganizationId == organizationId),
            filter,
            maxRecords,
            cancellationToken);

    private async Task<IReadOnlyList<AuditRecordEntry>> ListAsync(
        IQueryable<DbAuditRecord> records,
        AuditRecordFilter filter,
        int maxRecords,
        CancellationToken cancellationToken) =>
        await WithActorEmail(Filter(records, filter))
            // Newest first; the id breaks ties so the order and the cut at maxRecords are stable
            .OrderByDescending(row => row.Record.OccurredAt)
            .ThenByDescending(row => row.Record.Id)
            .Take(maxRecords)
            .Select(row => ToEntry(row.Record, row.ActorEmail))
            .ToListAsync(cancellationToken);

    // The optional filters, combined with AND
    private static IQueryable<DbAuditRecord> Filter(IQueryable<DbAuditRecord> records, AuditRecordFilter filter) =>
        records
            .Where(record => filter.ActorUserId == null || record.ActorUserId == filter.ActorUserId)
            .Where(record => filter.Action == null || record.Action == filter.Action)
            .Where(record => filter.From == null || record.OccurredAt >= filter.From)
            .Where(record => filter.To == null || record.OccurredAt <= filter.To);

    // A left join: the actor's email is the only thing returned about a user, and it is null for an
    // anonymous action or an account that no longer exists
    private IQueryable<AuditRecordRow> WithActorEmail(IQueryable<DbAuditRecord> records) =>
        from record in records
        join user in db.Users on record.ActorUserId equals (Guid?)user.Id into actors
        from actor in actors.DefaultIfEmpty()
        select new AuditRecordRow { Record = record, ActorEmail = actor == null ? null : actor.Email };

    // The database reads the time back without a kind; it was written in UTC
    private static AuditRecordEntry ToEntry(DbAuditRecord record, string? actorEmail) =>
        new(
            record.Id,
            DateTime.SpecifyKind(record.OccurredAt, DateTimeKind.Utc),
            record.ActorUserId,
            actorEmail,
            record.Action,
            record.TargetType,
            record.TargetId,
            record.Outcome,
            record.ErrorCode);

    // Built with an object initializer, so EF Core can order by its members
    private sealed class AuditRecordRow
    {
        public required DbAuditRecord Record { get; init; }
        public string? ActorEmail { get; init; }
    }
}

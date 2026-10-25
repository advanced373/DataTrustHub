using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.ListOrganizationAuditRecords;

// From and To are an inclusive range on OccurredAt, in UTC
public record ListOrganizationAuditRecordsQuery(
    Guid OrganizationId,
    Guid CallerId,
    Guid? UserId,
    string? Action,
    DateTime? From,
    DateTime? To);

// An organization admin reads their organization's audit trail (ADR-0016), newest first. The trail is read
// only here: no use case updates or deletes an audit record (A3)
public class ListOrganizationAuditRecordsHandler(IMembershipRepository memberships, DContext db)
    : IQueryHandler<ListOrganizationAuditRecordsQuery, Result<IReadOnlyList<AuditRecordSummary>>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly DContext _db = db;

    public async Task<Result<IReadOnlyList<AuditRecordSummary>>> Handle(
        ListOrganizationAuditRecordsQuery query,
        CancellationToken cancellationToken)
    {
        // Checked first: a caller who is not an organization admin there learns nothing about the trail
        if (!await _memberships.IsOrgAdminAsync(query.CallerId, query.OrganizationId, cancellationToken))
            return Result.Failure<IReadOnlyList<AuditRecordSummary>>(Errors.OrganizationNotFound);

        var records = _db.AuditRecords
            .AsNoTracking()
            .Where(r => r.OrganizationId == query.OrganizationId);

        var summaries = await WithActorEmail(Filter(records, query))
            // Newest first; the id breaks ties so the order and the cut at MaxRecords are stable
            .OrderByDescending(row => row.Record.OccurredAt)
            .ThenByDescending(row => row.Record.Id)
            .Take(Constants.MaxRecords)
            .Select(row => ToSummary(row.Record, row.ActorEmail))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<AuditRecordSummary>>(summaries);
    }

    // The optional filters, combined with AND
    private static IQueryable<DbAuditRecord> Filter(
        IQueryable<DbAuditRecord> records,
        ListOrganizationAuditRecordsQuery query)
    {
        var from = ToUtc(query.From);
        var to = ToUtc(query.To);

        return records
            .Where(r => query.UserId == null || r.ActorUserId == query.UserId)
            .Where(r => query.Action == null || r.Action == query.Action)
            .Where(r => from == null || r.OccurredAt >= from)
            .Where(r => to == null || r.OccurredAt <= to);
    }

    // A left join: the actor's email is the only thing returned about a user, and it is null for an
    // anonymous action or an account that no longer exists
    private IQueryable<AuditRecordRow> WithActorEmail(IQueryable<DbAuditRecord> records) =>
        from record in records
        join user in _db.Users on record.ActorUserId equals (Guid?)user.Id into actors
        from actor in actors.DefaultIfEmpty()
        select new AuditRecordRow { Record = record, ActorEmail = actor == null ? null : actor.Email };

    // The database reads the time back without a kind; it was written in UTC
    private static AuditRecordSummary ToSummary(DbAuditRecord record, string? actorEmail) =>
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

    // A filter time without a kind is taken as UTC; a local one is converted
    private static DateTime? ToUtc(DateTime? time) =>
        time is null ? null
        : time.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(time.Value, DateTimeKind.Utc)
        : time.Value.ToUniversalTime();

    // Built with an object initializer, so EF Core can order by its members
    private sealed class AuditRecordRow
    {
        public required DbAuditRecord Record { get; init; }
        public string? ActorEmail { get; init; }
    }
}

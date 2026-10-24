using System.Globalization;
using DataTrustHub.Services.Api;

namespace DataTrustHub.Models;

// The audit trail filters, bound from the query string: ids, action names and dates only (ADR-0019)
public sealed class AuditFilterModel
{
    public Guid? UserId { get; set; }

    public string? Action { get; set; }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    public bool IsActive => UserId is not null || !string.IsNullOrEmpty(Action) || From is not null || To is not null;

    // The values of the date inputs, so the filters are kept after a submit
    public string? FromInput => From?.ToString(AuditTrailDisplay.DateInputFormat, CultureInfo.InvariantCulture);

    public string? ToInput => To?.ToString(AuditTrailDisplay.DateInputFormat, CultureInfo.InvariantCulture);

    // The dates are UTC days: From starts at the beginning of its day and To ends at the end of its day,
    // so the range covers the whole of both
    public AuditRecordFilter ToApiFilter() => new(
        UserId,
        string.IsNullOrEmpty(Action) ? null : Action,
        From?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        To?.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));
}

// A user offered in the user filter; the value sent is the id, never the email (ADR-0019)
public sealed record AuditUserOption(Guid UserId, string Email);

// One audit record as the audit trail table shows it
public sealed record AuditRecordRow(
    string OccurredAt,
    string User,
    string Action,
    string? TargetType,
    Guid? TargetId,
    string? TargetShortId,
    string Outcome,
    string OutcomeBadgeClass,
    string? ErrorCode)
{
    internal static AuditRecordRow From(AuditRecordResponse record) =>
        new(
            record.OccurredAt.ToUniversalTime().ToString(AuditTrailDisplay.TimeFormat, CultureInfo.InvariantCulture),
            UserOf(record),
            record.Action,
            record.TargetType,
            record.TargetId,
            record.TargetId?.ToString()[..AuditTrailDisplay.ShortIdLength],
            record.Outcome,
            AuditOutcomes.BadgeClassOf(record.Outcome),
            record.ErrorCode);

    // The actor's email; an action without an actor is anonymous, and an actor without an email is an
    // account that no longer exists
    private static string UserOf(AuditRecordResponse record) =>
        record.ActorEmail ?? (record.ActorUserId is null ? Messages.AnonymousActor : Messages.DeletedAccount);
}

// The audit trail table and its filters, shared by the organization and the platform pages. RecordsLoaded
// is false when the records could not be loaded, so no empty state is shown; ErrorMessage is the failure
// that prevented loading the records or the user list
public sealed record AuditTrailViewModel(
    IReadOnlyList<AuditRecordRow> Records,
    bool RecordsLoaded,
    IReadOnlyList<AuditUserOption> Users,
    IReadOnlyList<string> Actions,
    AuditFilterModel Filter,
    string? ErrorMessage)
{
    // The API returns at most MaxRecords, so a full page may leave older records out
    public bool IsTruncated => Records.Count >= AuditTrailDisplay.MaxRecords;

    // Records is null when they could not be loaded
    internal static AuditTrailViewModel From(
        IReadOnlyList<AuditRecordResponse>? records,
        IEnumerable<AuditUserOption> users,
        IReadOnlyList<string> actions,
        AuditFilterModel filter,
        string? errorMessage) =>
        new(
            (records ?? []).Select(AuditRecordRow.From).ToList(),
            records is not null,
            users.OrderBy(user => user.Email, StringComparer.OrdinalIgnoreCase).ToList(),
            actions,
            filter,
            errorMessage);
}

// The audit trail page of one organization, for its organization admins
public sealed record OrganizationAuditViewModel(Guid OrganizationId, string OrganizationName, AuditTrailViewModel Trail);

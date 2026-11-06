using DataTrustHub.Domain.Auditing;
using DataTrustHub.Domain.ReadModels.DataItems;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.Extensions.Logging;

namespace DataTrustHub.Features.DataManagement.GetDataItem;

public record GetDataItemQuery(Guid DataItemId, Guid RequesterId);

// GET /data/{id} returns the content, so it is the download: every download and every denied read leaves an
// audit record (ADR-0016 point 2). The record is appended before the content is returned and a failed audit
// write propagates, so no content leaves without its record (fail closed). A denied read whose record
// cannot be written is logged and still answers 404, so a failing audit store does not reveal existence
public class GetDataItemHandler(
    IDataItemQueries dataItemQueries,
    IAuditLog auditLog,
    ILogger<GetDataItemHandler> logger)
    : IQueryHandler<GetDataItemQuery, Result<GetDataItemResponse>>
{
    private readonly IDataItemQueries _dataItemQueries = dataItemQueries;
    private readonly IAuditLog _auditLog = auditLog;
    private readonly ILogger<GetDataItemHandler> _logger = logger;

    // An item the requester may not read answers 404, so its existence is not revealed
    public async Task<Result<GetDataItemResponse>> Handle(
        GetDataItemQuery query,
        CancellationToken cancellationToken)
    {
        var readable = await _dataItemQueries.FindReadableAsync(
            query.DataItemId, query.RequesterId, cancellationToken);
        if (readable is not null)
        {
            await AppendAsync(query, readable.OrganizationId, AuditOutcome.Succeeded, null, cancellationToken);
            return Result.Success(ToResponse(readable));
        }

        await AppendDeniedIfExistsAsync(query, cancellationToken);
        return Result.Failure<GetDataItemResponse>(Errors.DataItemNotFound);
    }

    private static GetDataItemResponse ToResponse(ReadableDataItem item) =>
        new(
            item.Id,
            item.Name,
            item.Size,
            item.SecurityMarking,
            item.Content,
            item.ClassificationPriority,
            item.PolicyName,
            item.IsOwner,
            item.CanDelete);

    // A row that exists (deleted or not) but is hidden by the read rule is a denied read; a missing row is
    // not an access and leaves no record. The organization is set while the item's policy still exists
    private async Task AppendDeniedIfExistsAsync(GetDataItemQuery query, CancellationToken cancellationToken)
    {
        var hidden = await _dataItemQueries.FindDeniedReadTargetAsync(query.DataItemId, cancellationToken);

        if (hidden is null) return;

        await AppendDeniedSafelyAsync(query, hidden.OrganizationId, cancellationToken);
    }

    // No content leaves on the denied path, so a failed audit write is logged and the answer stays 404
    // (ADR-0011). A cancelled request is not swallowed. The exception itself is not logged: its message may
    // quote the record's values
    private async Task AppendDeniedSafelyAsync(
        GetDataItemQuery query,
        Guid? organizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await AppendAsync(
                query, organizationId, AuditOutcome.Denied, Errors.DataItemNotFound.Code, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                Constants.DeniedAuditWriteFailedMessage,
                Constants.DownloadAction,
                AuditOutcome.Denied,
                query.DataItemId,
                query.RequesterId,
                exception.GetType().Name);
        }
    }

    private Task AppendAsync(
        GetDataItemQuery query,
        Guid? organizationId,
        AuditOutcome outcome,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        var record = AuditRecord.Create(
            DateTime.UtcNow,
            query.RequesterId,
            Constants.DownloadAction,
            AuditTargetTypes.DataItem,
            query.DataItemId,
            organizationId,
            outcome,
            errorCode);

        return _auditLog.AppendAsync(record, cancellationToken);
    }
}

using DataTrustHub.Domain.ReadModels.DataItems;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.DataManagement.ListDataItems;

public record ListDataItemsQuery(Guid RequesterId);

public class ListDataItemsHandler(IDataItemQueries dataItemQueries)
    : IQueryHandler<ListDataItemsQuery, Result<IReadOnlyList<DataItemSummary>>>
{
    private readonly IDataItemQueries _dataItemQueries = dataItemQueries;

    // Own items plus those the requester's clearances allow; file content is left out, GET /data/{id} returns it
    public async Task<Result<IReadOnlyList<DataItemSummary>>> Handle(
        ListDataItemsQuery query,
        CancellationToken cancellationToken)
    {
        var entries = await _dataItemQueries.ListReadableAsync(query.RequesterId, cancellationToken);
        return Result.Success<IReadOnlyList<DataItemSummary>>(entries.Select(ToSummary).ToList());
    }

    private static DataItemSummary ToSummary(DataItemListEntry entry) =>
        new(
            entry.Id,
            entry.Name,
            entry.Size,
            entry.SecurityMarking,
            entry.ClassificationPriority,
            entry.PolicyId,
            entry.PolicyName,
            entry.OwnerEmail,
            entry.IsOwner,
            entry.CanDelete);
}

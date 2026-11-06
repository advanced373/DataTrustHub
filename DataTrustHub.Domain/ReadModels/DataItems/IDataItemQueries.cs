namespace DataTrustHub.Domain.ReadModels.DataItems;

// The read side of data items (ADR-0012 step 4): every method projects straight to a read model in SQL and
// applies the read rule (D3′) and the delete rule (D5′) in the database, never on loaded entities
public interface IDataItemQueries
{
    // The items the read rule lets the requester see, ordered by name; file content is left out
    Task<IReadOnlyList<DataItemListEntry>> ListReadableAsync(Guid requesterId, CancellationToken cancellationToken);

    // The item with its content, or null when it is missing or the read rule hides it from the requester
    Task<ReadableDataItem?> FindReadableAsync(Guid dataItemId, Guid requesterId, CancellationToken cancellationToken);

    // The item whatever the read rule says (deleted or not), for the record of a denied read (ADR-0016);
    // null when no row has this id
    Task<DeniedReadTarget?> FindDeniedReadTargetAsync(Guid dataItemId, CancellationToken cancellationToken);
}

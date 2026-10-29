namespace DataTrustHub.Domain.DataItems;

public interface IDataItemRepository
{
    Task<DataItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    // The owner's items that are not deleted yet
    Task<IReadOnlyList<DataItem>> ListActiveOwnedByAsync(Guid ownerUserId, CancellationToken cancellationToken);

    void Add(DataItem dataItem);

    Task UpdateAsync(DataItem dataItem, CancellationToken cancellationToken);
}

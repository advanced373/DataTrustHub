namespace DataTrustHub.Application.DataItem.Create
{
    public record CreateDataItemCommand(string Name, long Size, string? Content, Guid OwnerUserId, string SecurityMarking);
}

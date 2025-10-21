namespace DataTrustHub.Application.Data
{
    public record CreateDataItemCommand(string Name, long Size, string? Content, Guid OwnerUserId, string SecurityMarking);
}

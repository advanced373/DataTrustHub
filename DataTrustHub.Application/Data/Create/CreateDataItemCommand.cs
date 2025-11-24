using DataTrustHub.Application.Abstractions.Messaging;

namespace DataTrustHub.Application.Data.Create
{
    public record CreateDataItemCommand(string Name, long Size, string? Content, Guid OwnerUserId, string SecurityMarking) : ICommand<Guid>;
}

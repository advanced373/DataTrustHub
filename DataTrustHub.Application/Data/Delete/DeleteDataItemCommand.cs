using DataTrustHub.Application.Abstractions.Messaging;

namespace DataTrustHub.Application.Data.Delete
{
    public record DeleteDataItemCommand(Guid DataItemId) : ICommand;
}


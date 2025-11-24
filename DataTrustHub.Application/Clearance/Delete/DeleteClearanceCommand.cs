using DataTrustHub.Application.Abstractions.Messaging;

namespace DataTrustHub.Application.Clearance.Delete
{
    public record DeleteClearanceCommand(Guid ClearanceId) : ICommand;
}


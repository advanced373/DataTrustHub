using DataTrustHub.Application.Abstractions.Messaging;

namespace DataTrustHub.Application.Clearance.Create
{
    public record CreateClearanceForUserCommand(Guid UserId, Guid PolicyId, string Name): ICommand<Guid>;
}

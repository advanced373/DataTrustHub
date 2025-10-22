using DataTrustHub.Application.Abstractions.Messaging;

namespace DataTrustHub.Application.Policy.Create
{
    public record CreatePolicyCommand(string Name, Guid OrganizationId): ICommand<Guid>;
}

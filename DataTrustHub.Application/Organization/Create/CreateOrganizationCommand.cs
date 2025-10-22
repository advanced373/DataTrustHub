using DataTrustHub.Application.Abstractions.Messaging;

namespace DataTrustHub.Application.Organization.Create
{
    public record CreateOrganizationCommand(string Name): ICommand<Guid>;
}

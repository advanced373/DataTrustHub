using DataTrustHub.Application.Abstractions.Messaging;

namespace DataTrustHub.Application.Organization.Delete
{
    public record DeleteOrganizationCommand(Guid OrganizationId) : ICommand;
}


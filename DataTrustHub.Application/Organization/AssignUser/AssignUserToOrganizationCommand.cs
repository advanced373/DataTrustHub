using DataTrustHub.Application.Abstractions.Messaging;
using System.Windows.Input;

namespace DataTrustHub.Application.Organization.AssignUser
{
    public record AssignUserToOrganizationCommand(Guid UserId, Guid OrganizationId): ICommand<bool>;
}

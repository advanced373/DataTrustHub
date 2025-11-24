using DataTrustHub.Application.Abstractions.Messaging;

namespace DataTrustHub.Application.User.Delete
{
    public record DeleteUserCommand(Guid UserId) : ICommand;
}


using DataTrustHub.Application.Abstractions.Messaging;

namespace DataTrustHub.Application.User.Register
{
    public record RegisterUserCommand(string Email, string Password): ICommand<Guid>;
}

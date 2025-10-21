using DataTrustHub.Domain.User;
using MediatR;

namespace DataTrustHub.Application.User.Register
{
    public class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, Guid>
    {
        public async Task<Guid> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
        {
            // Linia de business logic simulată: creează și returnează un Guid nou
            var newUserId = Guid.NewGuid();
            // TODO: Persiste userul în data store
            return await Task.FromResult(newUserId);
        }
    }
}

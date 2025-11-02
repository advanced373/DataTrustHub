using DataTrustHub.Application.Abstractions.Data;
using DataTrustHub.Domain.User;
using DataTrustHub.SharedKernel;
using MediatR;

namespace DataTrustHub.Application.User.Register
{
    public class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, Result<Guid>>
    {
        public IUserRepository UserRepository { get; init; }
        public RegisterUserCommandHandler(IUserRepository userRepository)
        {
            UserRepository = userRepository;
        }

        public async Task<Result<Guid>> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
        {
            var newUserId = Guid.NewGuid();
            var result = await UserRepository.CreateUserAsync(newUserId.ToString(), cancellationToken);
            return await Task.FromResult(newUserId);
        }
    }
}

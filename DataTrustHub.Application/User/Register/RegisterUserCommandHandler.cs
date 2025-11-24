using UserEntity = DataTrustHub.Domain.User.User;
using DataTrustHub.Domain.User;
using DataTrustHub.SharedKernel;
using MediatR;

namespace DataTrustHub.Application.User.Register
{
    public class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, Result<Guid>>
    {
        public IUserRepository UserRepository { get; init; }
        public IPasswordHasher PasswordHasher { get; init; }
        public RegisterUserCommandHandler(IUserRepository userRepository, IPasswordHasher passwordHasher)
        {
            UserRepository = userRepository;
            PasswordHasher = passwordHasher;
        }

        public async Task<Result<Guid>> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
        {
            var newUserId = Guid.NewGuid();
            await UserRepository.AddAsync(new UserEntity
            {
                Id = newUserId,
                Email = request.Email,
                HashedPassword = PasswordHasher.HashPassword(request.Password)
            });
            return Result.Success(newUserId);
        }
    }
}

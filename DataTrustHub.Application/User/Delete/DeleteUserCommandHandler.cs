using DataTrustHub.Application.Abstractions.Messaging;
using DataTrustHub.Domain.User;
using DataTrustHub.SharedKernel;
using MediatR;

namespace DataTrustHub.Application.User.Delete
{
    public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand, Result>
    {
        private readonly IUserRepository _userRepository;

        public DeleteUserCommandHandler(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task<Result> Handle(DeleteUserCommand request, CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetByIdAsync(request.UserId);

            if (user is null)
            {
                return Result.Failure(UserErrors.NotFound(request.UserId));
            }

            await _userRepository.DeleteAsync(request.UserId);
            return Result.Success();
        }
    }
}


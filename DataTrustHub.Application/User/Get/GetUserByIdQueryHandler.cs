using DataTrustHub.Application.Abstractions.Messaging;
using DataTrustHub.Domain.User;
using DataTrustHub.SharedKernel;
using UserValue = DataTrustHub.Domain.User.User;

namespace DataTrustHub.Application.User.Get;

public sealed class GetUserByIdQueryHandler : IQueryHandler<GetUserByIdQuery, UserValue>
{
    private readonly IUserRepository _userRepository;

    public GetUserByIdQueryHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<Result<UserValue>> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId);

        return user is null
            ? Result.Failure<UserValue>(Error.NotFound("User.NotFound", $"User with ID {request.UserId} was not found."))
            : Result.Success(user);
    }
}

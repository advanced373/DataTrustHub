using DataTrustHub.Application.Abstractions.Messaging;
using DataTrustHub.Domain.User;
using DataTrustHub.SharedKernel;
using UserValue = DataTrustHub.Domain.User.User;

namespace DataTrustHub.Application.User.Get;

public sealed class GetUsersQueryHandler : IQueryHandler<GetUsersQuery, List<UserValue>>
{
    private readonly IUserRepository _userRepository;

    public GetUsersQueryHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<Result<List<UserValue>>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var users = await _userRepository.GetAllAsync();
        return Result.Success(users);
    }
}

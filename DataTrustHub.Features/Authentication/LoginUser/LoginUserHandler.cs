using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Jwt;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Authentication.LoginUser;

public record LoginUserCommand(string Email, string Password);

// Reads only, so it does not commit
public class LoginUserHandler(
    IUserRepository users,
    IPasswordHasher hasher,
    IJwtTokenGenerator jwtGenerator)
    : ICommandHandler<LoginUserCommand, Result<string>>
{
    private readonly IUserRepository _users = users;
    private readonly IPasswordHasher _hasher = hasher;
    private readonly IJwtTokenGenerator _jwtGenerator = jwtGenerator;

    public async Task<Result<string>> Handle(
        LoginUserCommand command,
        CancellationToken cancellationToken)
    {
        var user = await _users.GetByEmailAsync(command.Email, cancellationToken);
        if (user is null) return Result.Failure<string>(Errors.InvalidCredentials);

        if (!_hasher.VerifyHashedPassword(user.HashedPassword, command.Password))
            return Result.Failure<string>(Errors.InvalidCredentials);

        var token = _jwtGenerator.Generate(user.Id, user.Email, user.Role);
        return Result.Success(token);
    }
}

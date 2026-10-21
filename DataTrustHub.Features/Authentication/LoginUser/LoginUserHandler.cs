using DataTrustHub.Features._Shared.Jwt;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Authentication.LoginUser;

public record LoginUserCommand(string Email, string Password);

public class LoginUserHandler(
    DContext db,
    DataTrustHub.Domain.User.IPasswordHasher hasher,
    IJwtTokenGenerator jwtGenerator)
    : ICommandHandler<LoginUserCommand, Result<string>>
{
    private readonly DContext _db = db;
    private readonly DataTrustHub.Domain.User.IPasswordHasher _hasher = hasher;
    private readonly IJwtTokenGenerator _jwtGenerator = jwtGenerator;

    public async Task<Result<string>> Handle(
        LoginUserCommand command,
        CancellationToken cancellationToken)
    {
        var dbUser = await FindUserByEmail(command.Email, cancellationToken);
        if (dbUser is null) return Result.Failure<string>(Errors.InvalidCredentials);

        if (!_hasher.VerifyHashedPassword(dbUser.HashedPassword, command.Password))
            return Result.Failure<string>(Errors.InvalidCredentials);

        var token = _jwtGenerator.Generate(dbUser.Id, dbUser.Email, dbUser.Role);
        return Result.Success(token);
    }

    private Task<DbUser?> FindUserByEmail(string email, CancellationToken ct) =>
        _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
}

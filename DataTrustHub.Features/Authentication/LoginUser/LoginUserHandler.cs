using DataTrustHub.Features.Authentication._Shared;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Authentication.LoginUser;

public record LoginUserCommand(string Email, string Password) : IRequest<Result<string>>;

public class LoginUserHandler(
    DContext db,
    DataTrustHub.Domain.User.IPasswordHasher hasher,
    IJwtTokenGenerator jwtGenerator)
    : IRequestHandler<LoginUserCommand, Result<string>>
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

        var token = _jwtGenerator.Generate(dbUser.Id, dbUser.Email);
        return Result.Success(token);
    }

    private Task<DbUser?> FindUserByEmail(string email, CancellationToken ct) =>
        _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
}

using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.CreateAccount;

public record CreateAccountCommand(string Email, string Password) : IRequest<Result<Guid>>;

public class CreateAccountHandler(DContext db, DataTrustHub.Domain.User.IPasswordHasher hasher)
    : IRequestHandler<CreateAccountCommand, Result<Guid>>
{
    private readonly DContext _db = db;
    private readonly DataTrustHub.Domain.User.IPasswordHasher _hasher = hasher;

    public async Task<Result<Guid>> Handle(
        CreateAccountCommand command,
        CancellationToken cancellationToken)
    {
        var emailExists = await CheckEmailExists(command.Email, cancellationToken);
        if (emailExists) return Result.Failure<Guid>(Errors.EmailAlreadyInUse);

        var userId = Guid.NewGuid();
        await PersistUser(userId, command.Email, command.Password, cancellationToken);
        return Result.Success(userId);
    }

    private Task<bool> CheckEmailExists(string email, CancellationToken ct) =>
        _db.Users.AnyAsync(u => u.Email == email, ct);

    private async Task PersistUser(
        Guid userId, string email, string password, CancellationToken ct)
    {
        var dbUser = new DbUser
        {
            Id = userId,
            Email = email,
            HashedPassword = _hasher.HashPassword(password)
        };
        await _db.Users.AddAsync(dbUser, ct);
        await _db.SaveChangesAsync(ct);
    }
}

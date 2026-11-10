using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Jwt;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Authentication.LoginUser;

public record LoginUserCommand(string Email, string Password);

// Signs a user in. It commits only when the stored password hash is in an older format or weaker than
// configured: the password is hashed again and the new hash replaces it, in one save (U8)
public class LoginUserHandler(
    IUserRepository users,
    IPasswordHasher hasher,
    IJwtTokenGenerator jwtGenerator,
    IUnitOfWork unitOfWork,
    AuditScope auditScope)
    : ICommandHandler<LoginUserCommand, Result<string>>
{
    private readonly IUserRepository _users = users;
    private readonly IPasswordHasher _hasher = hasher;
    private readonly IJwtTokenGenerator _jwtGenerator = jwtGenerator;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly AuditScope _auditScope = auditScope;

    public async Task<Result<string>> Handle(
        LoginUserCommand command,
        CancellationToken cancellationToken)
    {
        var user = await _users.GetByEmailAsync(command.Email, cancellationToken);
        if (user is null)
        {
            // Spends the same hashing work as a real check, so the response time does not reveal that the
            // email has no account (U4, ADR-0025)
            _hasher.HashPassword(command.Password);
            return Result.Failure<string>(Errors.InvalidCredentials);
        }

        var verification = _hasher.VerifyHashedPassword(user.HashedPassword, command.Password);
        if (verification == PasswordVerificationResult.Failed)
            return Result.Failure<string>(Errors.InvalidCredentials);

        // The password is verified: the sign-in is recorded with the signed-in user as its actor
        _auditScope.SetActor(user.Id);

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            await ReplacePasswordHashAsync(user, command.Password, cancellationToken);

        var token = _jwtGenerator.Generate(user.Id, user.Email, user.Role);
        return Result.Success(token);
    }

    // Re-hash on sign-in (U8); a failed save propagates
    private async Task ReplacePasswordHashAsync(User user, string password, CancellationToken cancellationToken)
    {
        user.ReplacePasswordHash(_hasher.HashPassword(password));
        await _users.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

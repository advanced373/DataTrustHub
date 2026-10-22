using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Authentication.RegisterUser;

public record RegisterUserCommand(string Email, string Password);

public class RegisterUserHandler(
    IUserRepository users,
    IPasswordHasher hasher,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RegisterUserCommand, Result<Guid>>
{
    private readonly IUserRepository _users = users;
    private readonly IPasswordHasher _hasher = hasher;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Result<Guid>> Handle(
        RegisterUserCommand command,
        CancellationToken cancellationToken)
    {
        var emailExists = await _users.EmailExistsAsync(command.Email, cancellationToken);
        if (emailExists) return Result.Failure<Guid>(Errors.EmailAlreadyInUse);

        var user = User.Register(command.Email, _hasher.HashPassword(command.Password));
        _users.Add(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(user.Id);
    }
}

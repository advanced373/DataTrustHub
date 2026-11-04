using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Registrations;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features._Shared.Registration;
using DataTrustHub.SharedKernel;
using Microsoft.Extensions.Options;

namespace DataTrustHub.Features.Authentication.RegisterUser;

public record RegisterUserCommand(string Email, string Password);

// Submits a registration request; no user is created until it is approved (ADR-0026). Logs nothing, so the
// email and the password never reach a log
public class RegisterUserHandler(
    IUserRepository users,
    IRegistrationRequestRepository registrationRequests,
    IPasswordHasher hasher,
    IUnitOfWork unitOfWork,
    IOptions<RegistrationOptions> options,
    TimeProvider timeProvider)
    : ICommandHandler<RegisterUserCommand, Result<Guid>>
{
    private readonly IUserRepository _users = users;
    private readonly IRegistrationRequestRepository _registrationRequests = registrationRequests;
    private readonly IPasswordHasher _hasher = hasher;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly RegistrationOptions _options = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<Result<Guid>> Handle(
        RegisterUserCommand command,
        CancellationToken cancellationToken)
    {
        // R2: expired requests are purged lazily, here, so they neither block their email nor fill the queue.
        // The delete runs at once, outside the unit of work
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await _registrationRequests.RemoveExpiredAsync(now - _options.PendingRequestLifetime, cancellationToken);

        var refusal = await FindRefusalAsync(command.Email, cancellationToken);
        if (refusal is not null) return Result.Failure<Guid>(refusal);

        var request = RegistrationRequest.Submit(command.Email, _hasher.HashPassword(command.Password), now);
        _registrationRequests.Add(request);
        return await SaveRequest(request, cancellationToken);
    }

    // U1, then R1, then R3; null when the request can be submitted
    private async Task<Error?> FindRefusalAsync(string email, CancellationToken cancellationToken)
    {
        if (await _users.EmailExistsAsync(email, cancellationToken)) return Errors.EmailAlreadyInUse;

        if (await _registrationRequests.EmailExistsAsync(email, cancellationToken))
            return Errors.RegistrationAlreadyPending;

        var pendingCount = await _registrationRequests.CountAsync(cancellationToken);
        return pendingCount >= _options.MaxPendingRequests ? Errors.RegistrationQueueFull : null;
    }

    // R1 under concurrency: a request for the same email that passed EmailExistsAsync at the same time saved
    // first, and the unique index rejects this one
    private async Task<Result<Guid>> SaveRequest(RegistrationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(request.Id);
        }
        catch (UniqueConstraintViolationException)
        {
            return Result.Failure<Guid>(Errors.RegistrationAlreadyPending);
        }
    }
}

using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Registrations;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features._Shared.Registration;
using DataTrustHub.SharedKernel;
using Microsoft.Extensions.Options;

namespace DataTrustHub.Features.Administration.ApproveRegistrationRequest;

// Only the request's id is in the command, so the audit record holds no email
public record ApproveRegistrationRequestCommand(Guid RegistrationRequestId) : ITargetedCommand
{
    AuditTarget ITargetedCommand.Target => new(AuditTargetTypes.RegistrationRequest, RegistrationRequestId);
}

// Creates the user from a pending registration request and removes the request, in one save (R5, ADR-0026).
// Logs nothing, so the email and the hash never reach a log
public class ApproveRegistrationRequestHandler(
    IRegistrationRequestRepository registrationRequests,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IOptions<RegistrationOptions> options,
    TimeProvider timeProvider)
    : ICommandHandler<ApproveRegistrationRequestCommand, Result<Guid>>
{
    private readonly IRegistrationRequestRepository _registrationRequests = registrationRequests;
    private readonly IUserRepository _users = users;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly RegistrationOptions _options = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<Result<Guid>> Handle(
        ApproveRegistrationRequestCommand command,
        CancellationToken cancellationToken)
    {
        var request = await _registrationRequests.GetByIdAsync(command.RegistrationRequestId, cancellationToken);
        if (request is null) return Result.Failure<Guid>(RegistrationRequestErrors.NotFound);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var approval = request.Approve(now, _options.PendingRequestLifetime);
        if (approval.IsFailure) return Result.Failure<Guid>(approval.Error);

        // U1: the request stays, so the platform admin can reject it
        if (await _users.EmailExistsAsync(request.Email, cancellationToken))
            return Result.Failure<Guid>(Errors.EmailAlreadyInUse);

        _users.Add(approval.Value);
        await _registrationRequests.RemoveAsync(request, cancellationToken);
        return await SaveUser(approval.Value, cancellationToken);
    }

    // U1 under concurrency: an account for the same email was saved after EmailExistsAsync, and the unique
    // index rejects this one; nothing is saved, so the request stays
    private async Task<Result<Guid>> SaveUser(User user, CancellationToken cancellationToken)
    {
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(user.Id);
        }
        catch (UniqueConstraintViolationException)
        {
            return Result.Failure<Guid>(Errors.EmailAlreadyInUse);
        }
    }
}

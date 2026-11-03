using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Registrations;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.RejectRegistrationRequest;

// Only the request's id is in the command, so the audit record holds no email
public record RejectRegistrationRequestCommand(Guid RegistrationRequestId) : ITargetedCommand
{
    AuditTarget ITargetedCommand.Target => new(AuditTargetTypes.RegistrationRequest, RegistrationRequestId);
}

// Deletes a registration request, expired or not; its email can then register again (ADR-0026)
public class RejectRegistrationRequestHandler(
    IRegistrationRequestRepository registrationRequests,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RejectRegistrationRequestCommand, Result<Guid>>
{
    private readonly IRegistrationRequestRepository _registrationRequests = registrationRequests;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Result<Guid>> Handle(
        RejectRegistrationRequestCommand command,
        CancellationToken cancellationToken)
    {
        var request = await _registrationRequests.GetByIdAsync(command.RegistrationRequestId, cancellationToken);
        if (request is null) return Result.Failure<Guid>(RegistrationRequestErrors.NotFound);

        await _registrationRequests.RemoveAsync(request, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(command.RegistrationRequestId);
    }
}

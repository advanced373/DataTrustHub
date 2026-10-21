using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.DeletePolicy;

public record DeletePolicyCommand(Guid PolicyId);

public class DeletePolicyHandler(
    IPolicyRepository policies,
    IClearanceRepository clearances,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeletePolicyCommand, Result<Guid>>
{
    private readonly IPolicyRepository _policies = policies;
    private readonly IClearanceRepository _clearances = clearances;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    // Clearances reference policies by id without a foreign key, so the check is done here
    public async Task<Result<Guid>> Handle(
        DeletePolicyCommand command,
        CancellationToken cancellationToken)
    {
        var policy = await _policies.GetByIdAsync(command.PolicyId, cancellationToken);
        if (policy is null) return Result.Failure<Guid>(Errors.PolicyNotFound);

        var hasClearances = await _clearances.AnyForPolicyAsync(command.PolicyId, cancellationToken);
        if (hasClearances) return Result.Failure<Guid>(Errors.PolicyHasClearances);

        await _policies.RemoveAsync(policy, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(command.PolicyId);
    }
}

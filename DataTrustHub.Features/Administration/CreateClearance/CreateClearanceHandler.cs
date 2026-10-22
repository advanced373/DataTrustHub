using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.CreateClearance;

public record CreateClearanceCommand(
    Guid UserId,
    Guid PolicyId,
    string Name,
    string ClassificationLevelName,
    int ClassificationLevelPriority);

public class CreateClearanceHandler(
    IUserRepository users,
    IPolicyRepository policies,
    IClearanceRepository clearances,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CreateClearanceCommand, Result<Guid>>
{
    private readonly IUserRepository _users = users;
    private readonly IPolicyRepository _policies = policies;
    private readonly IClearanceRepository _clearances = clearances;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Result<Guid>> Handle(
        CreateClearanceCommand command,
        CancellationToken cancellationToken)
    {
        if (!await _users.ExistsAsync(command.UserId, cancellationToken))
            return Result.Failure<Guid>(Errors.UserNotFound);

        if (!await _policies.ExistsAsync(command.PolicyId, cancellationToken))
            return Result.Failure<Guid>(Errors.PolicyNotFound);

        var alreadyGranted = await _clearances.ExistsAsync(command.UserId, command.PolicyId, cancellationToken);
        if (alreadyGranted) return Result.Failure<Guid>(Errors.AlreadyGranted);

        var clearance = GrantClearance(command);
        await _clearances.AddAsync(clearance, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(clearance.Id);
    }

    private static Clearance GrantClearance(CreateClearanceCommand command) =>
        Clearance.Grant(
            command.Name,
            command.UserId,
            command.PolicyId,
            new ClassificationLevel(command.ClassificationLevelName, command.ClassificationLevelPriority));
}

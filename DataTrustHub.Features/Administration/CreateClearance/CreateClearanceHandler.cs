using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.CreateClearance;

public record CreateClearanceCommand(
    Guid OrganizationId,
    Guid CallerId,
    Guid UserId,
    Guid PolicyId,
    string Name,
    string ClassificationLevelName,
    int ClassificationLevelPriority)
    : IOrganizationScopedCommand;

// An organization admin grants a clearance on a policy of their organization (C6) to a member or guest
// there (C5), never to themselves (C8)
public class CreateClearanceHandler(
    IMembershipRepository memberships,
    IUserRepository users,
    IPolicyRepository policies,
    IClearanceRepository clearances,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CreateClearanceCommand, Result<Guid>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IUserRepository _users = users;
    private readonly IPolicyRepository _policies = policies;
    private readonly IClearanceRepository _clearances = clearances;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Result<Guid>> Handle(
        CreateClearanceCommand command,
        CancellationToken cancellationToken)
    {
        var refusal = await FindRefusalAsync(command, cancellationToken);
        if (refusal is not null) return Result.Failure<Guid>(refusal);

        var clearance = GrantClearance(command);
        await _clearances.AddAsync(clearance, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(clearance.Id);
    }

    // The checks in the order of the contract; null when the clearance may be granted
    private async Task<Error?> FindRefusalAsync(CreateClearanceCommand command, CancellationToken cancellationToken)
    {
        if (!await _memberships.IsOrgAdminAsync(command.CallerId, command.OrganizationId, cancellationToken))
            return Errors.OrganizationNotFound;

        // Invariant C2; a policy of another organization is not found, so its existence is not revealed
        if (!await IsPolicyOfOrganizationAsync(command, cancellationToken)) return Errors.PolicyNotFound;
        if (!await _users.ExistsAsync(command.UserId, cancellationToken)) return Errors.UserNotFound;

        // Invariant C5: any membership counts (member, guest or organization admin)
        if (!await _memberships.ExistsAsync(command.UserId, command.OrganizationId, cancellationToken))
            return Errors.UserNotMember;

        // Invariant C8: another organization admin of the organization must grant it
        if (command.UserId == command.CallerId) return Errors.CannotGrantSelf;

        // Invariant C3
        var alreadyGranted = await _clearances.ExistsAsync(command.UserId, command.PolicyId, cancellationToken);
        return alreadyGranted ? Errors.AlreadyGranted : null;
    }

    private async Task<bool> IsPolicyOfOrganizationAsync(
        CreateClearanceCommand command,
        CancellationToken cancellationToken)
    {
        var policy = await _policies.GetByIdAsync(command.PolicyId, cancellationToken);
        return policy is not null && policy.OrganizationId == command.OrganizationId;
    }

    private static Clearance GrantClearance(CreateClearanceCommand command) =>
        Clearance.Grant(
            command.Name,
            command.UserId,
            command.PolicyId,
            new ClassificationLevel(command.ClassificationLevelName, command.ClassificationLevelPriority));
}

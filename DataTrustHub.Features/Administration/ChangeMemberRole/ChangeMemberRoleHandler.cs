using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.Features.Administration.MembershipRules;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.ChangeMemberRole;

// Role is Member or OrgAdmin (checked by the validator)
public record ChangeMemberRoleCommand(Guid OrganizationId, Guid CallerId, Guid UserId, string Role)
    : IOrganizationScopedCommand, ITargetedCommand
{
    AuditTarget ITargetedCommand.Target => new(AuditTargetTypes.User, UserId);
}

// An organization admin changes a member's role. This is how further organization admins are appointed
// (ADR-0014); an organization admin may also demote themselves while another one remains (M2)
public class ChangeMemberRoleHandler(
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ChangeMemberRoleCommand, Result<Guid>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Result<Guid>> Handle(
        ChangeMemberRoleCommand command,
        CancellationToken cancellationToken)
    {
        if (!await _memberships.IsOrgAdminAsync(command.CallerId, command.OrganizationId, cancellationToken))
            return Result.Failure<Guid>(Errors.OrganizationNotFound);

        var membership = await _memberships.GetAsync(command.UserId, command.OrganizationId, cancellationToken);
        if (membership is null) return Result.Failure<Guid>(Errors.MembershipNotFound);

        // The validator allows Member and OrgAdmin only; any other standard role, such as Guest, still
        // reaches the entity, which keeps the guest role fixed (M3)
        var newRole = MembershipRole.FindStandard(command.Role);
        if (newRole is null) return Result.Failure<Guid>(Errors.UnknownRole);
        if (newRole == membership.Role) return Result.Success(membership.Id);

        // Any change of an organization admin's role is a demotion (M2)
        if (await _memberships.IsLastOrgAdminAsync(membership, cancellationToken))
            return Result.Failure<Guid>(Errors.LastOrgAdmin);

        // A guest's membership is refused by the entity (M3)
        var roleChanged = membership.ChangeRole(newRole);
        if (roleChanged.IsFailure) return Result.Failure<Guid>(roleChanged.Error);

        await _memberships.UpdateAsync(membership, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(membership.Id);
    }
}

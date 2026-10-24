using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.Organizations;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.AppointOrganizationAdmin;

public record AppointOrganizationAdminCommand(Guid OrganizationId, Guid CallerId, Guid UserId);

// The platform admin appoints the first organization admin of an organization, never themselves
// (ADR-0014). Further organization admins are appointed by the organization's own admins, by changing a
// member's role (ChangeMemberRole). It gives the platform admin no access to the organization itself
public class AppointOrganizationAdminHandler(
    IOrganizationRepository organizations,
    IUserRepository users,
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork)
    : ICommandHandler<AppointOrganizationAdminCommand, Result<Guid>>
{
    private readonly IOrganizationRepository _organizations = organizations;
    private readonly IUserRepository _users = users;
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Result<Guid>> Handle(
        AppointOrganizationAdminCommand command,
        CancellationToken cancellationToken)
    {
        if (!await _organizations.ExistsAsync(command.OrganizationId, cancellationToken))
            return Result.Failure<Guid>(Errors.OrganizationNotFound);

        if (!await _users.ExistsAsync(command.UserId, cancellationToken))
            return Result.Failure<Guid>(Errors.UserNotFound);

        if (command.UserId == command.CallerId) return Result.Failure<Guid>(Errors.CannotAppointSelf);

        if (await _memberships.CountOrgAdminsAsync(command.OrganizationId, cancellationToken) > 0)
            return Result.Failure<Guid>(Errors.AlreadyHasOrgAdmin);

        var membership = await _memberships.GetAsync(command.UserId, command.OrganizationId, cancellationToken);
        return membership is null
            ? await AppointNewMember(command, cancellationToken)
            : await PromoteExistingMember(membership, cancellationToken);
    }

    private async Task<Result<Guid>> AppointNewMember(
        AppointOrganizationAdminCommand command,
        CancellationToken cancellationToken)
    {
        var membership = Membership.AppointOrgAdmin(command.UserId, command.OrganizationId);
        _memberships.Add(membership);
        return await SaveNewMembership(membership, cancellationToken);
    }

    // M1 under concurrency: a request at the same time already created the membership, and the unique
    // index rejects this one; a retry gets Organization.AlreadyHasOrgAdmin
    private async Task<Result<Guid>> SaveNewMembership(Membership membership, CancellationToken cancellationToken)
    {
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(membership.Id);
        }
        catch (UniqueConstraintViolationException)
        {
            return Result.Failure<Guid>(Errors.AlreadyMember);
        }
    }

    // A member becomes organization admin; a guest is refused by the entity (M3)
    private async Task<Result<Guid>> PromoteExistingMember(Membership membership, CancellationToken cancellationToken)
    {
        var roleChanged = membership.ChangeRole(MembershipRole.OrgAdmin);
        if (roleChanged.IsFailure) return Result.Failure<Guid>(roleChanged.Error);

        await _memberships.UpdateAsync(membership, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(membership.Id);
    }
}

using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.AddMember;

// Role is Member or Guest (checked by the validator)
public record AddMemberCommand(Guid OrganizationId, Guid CallerId, Guid UserId, string Role)
    : IOrganizationScopedCommand, ITargetedCommand
{
    AuditTarget ITargetedCommand.Target => new(AuditTargetTypes.User, UserId);
}

public class AddMemberHandler(
    IUserRepository users,
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork)
    : ICommandHandler<AddMemberCommand, Result<Guid>>
{
    private readonly IUserRepository _users = users;
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Result<Guid>> Handle(
        AddMemberCommand command,
        CancellationToken cancellationToken)
    {
        if (!await _memberships.IsOrgAdminAsync(command.CallerId, command.OrganizationId, cancellationToken))
            return Result.Failure<Guid>(Errors.OrganizationNotFound);

        if (!await _users.ExistsAsync(command.UserId, cancellationToken))
            return Result.Failure<Guid>(Errors.UserNotFound);

        // Invariant M1
        var alreadyMember = await _memberships.ExistsAsync(command.UserId, command.OrganizationId, cancellationToken);
        if (alreadyMember) return Result.Failure<Guid>(Errors.AlreadyMember);

        var membership = CreateMembership(command);
        _memberships.Add(membership);
        return await SaveMembership(membership, cancellationToken);
    }

    private static Membership CreateMembership(AddMemberCommand command) =>
        command.Role == MembershipRole.Guest.Name
            ? Membership.AddGuest(command.UserId, command.OrganizationId)
            : Membership.AddMember(command.UserId, command.OrganizationId);

    // M1 under concurrency: a request that passed ExistsAsync at the same time saved first, and the
    // unique index rejects this one
    private async Task<Result<Guid>> SaveMembership(Membership membership, CancellationToken cancellationToken)
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
}

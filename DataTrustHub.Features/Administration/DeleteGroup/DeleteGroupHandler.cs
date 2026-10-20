using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.DeleteGroup;

public record DeleteGroupCommand(Guid GroupId) : IRequest<Result<Guid>>;

public class DeleteGroupHandler(DContext db) : IRequestHandler<DeleteGroupCommand, Result<Guid>>
{
    private readonly DContext _db = db;

    // Policies reference groups by id without a foreign key, so the check is done here
    public async Task<Result<Guid>> Handle(
        DeleteGroupCommand command,
        CancellationToken cancellationToken)
    {
        var group = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == command.GroupId, cancellationToken);
        if (group is null) return Result.Failure<Guid>(Errors.GroupNotFound);

        var hasPolicies = await _db.Policies.AnyAsync(p => p.OrganizationId == command.GroupId, cancellationToken);
        if (hasPolicies) return Result.Failure<Guid>(Errors.GroupHasPolicies);

        _db.Organizations.Remove(group);
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(command.GroupId);
    }
}

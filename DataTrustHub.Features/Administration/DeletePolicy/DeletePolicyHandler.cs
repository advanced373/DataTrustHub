using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.DeletePolicy;

public record DeletePolicyCommand(Guid PolicyId);

public class DeletePolicyHandler(DContext db) : ICommandHandler<DeletePolicyCommand, Result<Guid>>
{
    private readonly DContext _db = db;

    // Clearances reference policies by id without a foreign key, so the check is done here
    public async Task<Result<Guid>> Handle(
        DeletePolicyCommand command,
        CancellationToken cancellationToken)
    {
        var policy = await _db.Policies.FirstOrDefaultAsync(p => p.Id == command.PolicyId, cancellationToken);
        if (policy is null) return Result.Failure<Guid>(Errors.PolicyNotFound);

        var hasClearances = await _db.Clearances.AnyAsync(c => c.PolicyId == command.PolicyId, cancellationToken);
        if (hasClearances) return Result.Failure<Guid>(Errors.PolicyHasClearances);

        _db.Policies.Remove(policy);
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(command.PolicyId);
    }
}

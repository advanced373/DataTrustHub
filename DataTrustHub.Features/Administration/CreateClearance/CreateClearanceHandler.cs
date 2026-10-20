using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.CreateClearance;

public record CreateClearanceCommand(
    Guid UserId,
    Guid PolicyId,
    string Name,
    string ClassificationLevelName,
    int ClassificationLevelPriority) : IRequest<Result<Guid>>;

public class CreateClearanceHandler(DContext db) : IRequestHandler<CreateClearanceCommand, Result<Guid>>
{
    private readonly DContext _db = db;

    public async Task<Result<Guid>> Handle(
        CreateClearanceCommand command,
        CancellationToken cancellationToken)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == command.UserId, cancellationToken))
            return Result.Failure<Guid>(Errors.UserNotFound);

        if (!await _db.Policies.AnyAsync(p => p.Id == command.PolicyId, cancellationToken))
            return Result.Failure<Guid>(Errors.PolicyNotFound);

        var alreadyGranted = await _db.Clearances.AnyAsync(
            c => c.UserId == command.UserId && c.PolicyId == command.PolicyId, cancellationToken);
        if (alreadyGranted) return Result.Failure<Guid>(Errors.AlreadyGranted);

        var clearanceId = Guid.NewGuid();
        await PersistClearance(clearanceId, command, cancellationToken);
        return Result.Success(clearanceId);
    }

    private async Task PersistClearance(Guid clearanceId, CreateClearanceCommand command, CancellationToken ct)
    {
        var dbClearance = new DbClearance
        {
            Id = clearanceId,
            Name = command.Name,
            UserId = command.UserId,
            PolicyId = command.PolicyId,
            ClassificationLevel = await FindOrCreateLevel(command, ct)
        };
        await _db.Clearances.AddAsync(dbClearance, ct);
        await _db.SaveChangesAsync(ct);
    }

    // Reuses a matching level, so granting many clearances does not duplicate classification rows
    private async Task<DbClassificationLevel> FindOrCreateLevel(CreateClearanceCommand command, CancellationToken ct) =>
        await _db.ClassificationLevels.FirstOrDefaultAsync(
            l => l.Name == command.ClassificationLevelName && l.Priority == command.ClassificationLevelPriority, ct)
        ?? new DbClassificationLevel
        {
            Name = command.ClassificationLevelName,
            Priority = command.ClassificationLevelPriority
        };
}

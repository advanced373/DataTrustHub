using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.DataManagement.AddDataItem;

// PolicyId and LevelPriority are both set for a classified item and both null for a personal one
public record AddDataItemCommand(IFormFile? File, Guid OwnerUserId, Guid? PolicyId = null, int? LevelPriority = null);

public class AddDataItemHandler(DContext db) : ICommandHandler<AddDataItemCommand, Result<Guid>>
{
    private readonly DContext _db = db;

    public async Task<Result<Guid>> Handle(
        AddDataItemCommand command,
        CancellationToken cancellationToken)
    {
        if (command.File is null || command.File.Length == 0)
            return Result.Failure<Guid>(Errors.NoFileProvided);

        if (command.File.Length > Constants.MaxFileSizeBytes)
            return Result.Failure<Guid>(Errors.FileTooLarge);

        var ownerExists = await CheckUserExists(command.OwnerUserId, cancellationToken);
        if (!ownerExists) return Result.Failure<Guid>(Errors.UserNotFound);

        var level = command.LevelPriority is int priority ? ClassificationLevels.FindByPriority(priority) : null;
        if (level is not null && !await HoldsClearance(command, level, cancellationToken))
            return Result.Failure<Guid>(Errors.NotCleared);

        var content = await ReadFileContent(command.File, cancellationToken);
        var itemId = Guid.NewGuid();
        await PersistDataItem(itemId, command, level, content, cancellationToken);
        return Result.Success(itemId);
    }

    private Task<bool> CheckUserExists(Guid userId, CancellationToken ct) =>
        _db.Users.AnyAsync(u => u.Id == userId, ct);

    // An owner may classify only under a policy they are cleared on, and at most at their own level
    private Task<bool> HoldsClearance(AddDataItemCommand command, ClassificationLevel level, CancellationToken ct) =>
        _db.Clearances.AnyAsync(
            c => c.UserId == command.OwnerUserId
                 && c.PolicyId == command.PolicyId
                 && c.ClassificationLevel.Priority >= level.Priority,
            ct);

    private static async Task<byte[]> ReadFileContent(IFormFile file, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        return stream.ToArray();
    }

    private async Task PersistDataItem(
        Guid itemId,
        AddDataItemCommand command,
        ClassificationLevel? level,
        byte[] content,
        CancellationToken ct)
    {
        var dbItem = new DbDataItem
        {
            Id = itemId,
            Name = command.File!.FileName,
            Size = command.File.Length,
            Content = Convert.ToBase64String(content),
            OwnerUserId = command.OwnerUserId,
            SecurityMarking = level?.Name ?? ClassificationLevels.PersonalMarking,
            PolicyId = level is null ? null : command.PolicyId,
            ClassificationPriority = level?.Priority
        };
        await _db.DataItems.AddAsync(dbItem, ct);
        await _db.SaveChangesAsync(ct);
    }
}

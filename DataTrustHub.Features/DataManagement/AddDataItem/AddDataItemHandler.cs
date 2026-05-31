using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.DataManagement.AddDataItem;

public record AddDataItemCommand(IFormFile? File, Guid OwnerUserId) : IRequest<Result<Guid>>;

public class AddDataItemHandler(DContext db) : IRequestHandler<AddDataItemCommand, Result<Guid>>
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

        var content = await ReadFileContent(command.File, cancellationToken);
        var itemId = Guid.NewGuid();
        await PersistDataItem(itemId, command.File, content, command.OwnerUserId, cancellationToken);
        return Result.Success(itemId);
    }

    private Task<bool> CheckUserExists(Guid userId, CancellationToken ct) =>
        _db.Users.AnyAsync(u => u.Id == userId, ct);

    private static async Task<byte[]> ReadFileContent(IFormFile file, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        return stream.ToArray();
    }

    private async Task PersistDataItem(
        Guid itemId,
        IFormFile file,
        byte[] content,
        Guid ownerId,
        CancellationToken ct)
    {
        var dbItem = new DbDataItem
        {
            Id = itemId,
            Name = file.FileName,
            Size = file.Length,
            Content = Convert.ToBase64String(content),
            OwnerUserId = ownerId,
            SecurityMarking = "UNCLASSIFIED"
        };
        await _db.DataItems.AddAsync(dbItem, ct);
        await _db.SaveChangesAsync(ct);
    }
}

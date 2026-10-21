using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.DataItems;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace DataTrustHub.Features.DataManagement.AddDataItem;

// PolicyId and LevelPriority are both set for a classified item and both null for a personal one
public record AddDataItemCommand(IFormFile? File, Guid OwnerUserId, Guid? PolicyId = null, int? LevelPriority = null);

public class AddDataItemHandler(
    IUserRepository users,
    IClearanceRepository clearances,
    IDataItemRepository dataItems,
    IUnitOfWork unitOfWork) : ICommandHandler<AddDataItemCommand, Result<Guid>>
{
    private readonly IUserRepository _users = users;
    private readonly IClearanceRepository _clearances = clearances;
    private readonly IDataItemRepository _dataItems = dataItems;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Result<Guid>> Handle(
        AddDataItemCommand command,
        CancellationToken cancellationToken)
    {
        if (command.File is null || command.File.Length == 0)
            return Result.Failure<Guid>(Errors.NoFileProvided);

        if (command.File.Length > Constants.MaxFileSizeBytes)
            return Result.Failure<Guid>(Errors.FileTooLarge);

        if (!await _users.ExistsAsync(command.OwnerUserId, cancellationToken))
            return Result.Failure<Guid>(Errors.UserNotFound);

        var content = await ReadFileContent(command.File, cancellationToken);
        var created = await CreateDataItem(command, command.File, content, cancellationToken);
        if (created.IsFailure) return Result.Failure<Guid>(created.Error);

        _dataItems.Add(created.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(created.Value.Id);
    }

    // A personal item when no classification is given; otherwise the owner's clearances decide
    private async Task<Result<DataItem>> CreateDataItem(
        AddDataItemCommand command,
        IFormFile file,
        string content,
        CancellationToken ct)
    {
        var level = command.LevelPriority is int priority ? ClassificationLevel.FindStandard(priority) : null;
        if (level is null || command.PolicyId is not Guid policyId)
            return Result.Success(DataItem.CreatePersonal(file.FileName, file.Length, content, command.OwnerUserId));

        var ownerClearances = await _clearances.ListForUserAsync(command.OwnerUserId, ct);
        return DataItem.CreateClassified(
            file.FileName, file.Length, content, command.OwnerUserId, policyId, level, ownerClearances);
    }

    // Stored as base64 of the file bytes
    private static async Task<string> ReadFileContent(IFormFile file, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        return Convert.ToBase64String(stream.ToArray());
    }
}

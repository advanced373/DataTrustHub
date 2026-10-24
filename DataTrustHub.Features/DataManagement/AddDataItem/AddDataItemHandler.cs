using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.DataItems;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Auditing;
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
    IPolicyRepository policies,
    IUnitOfWork unitOfWork,
    AuditScope auditScope) : ICommandHandler<AddDataItemCommand, Result<Guid>>
{
    private readonly IUserRepository _users = users;
    private readonly IClearanceRepository _clearances = clearances;
    private readonly IDataItemRepository _dataItems = dataItems;
    private readonly IPolicyRepository _policies = policies;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly AuditScope _auditScope = auditScope;

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

        var organizationId = await FindOrganizationIdAsync(created.Value, cancellationToken);
        _dataItems.Add(created.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        RecordInAuditScope(created.Value, organizationId);
        return Result.Success(created.Value.Id);
    }

    // A classified item's organization is its policy's; a personal item, or a policy gone, has none
    private async Task<Guid?> FindOrganizationIdAsync(DataItem dataItem, CancellationToken ct)
    {
        if (dataItem.PolicyId is not Guid policyId) return null;

        var policy = await _policies.GetByIdAsync(policyId, ct);
        return policy?.OrganizationId;
    }

    // The new item exists only once saved, so the audit trail learns its target here
    private void RecordInAuditScope(DataItem dataItem, Guid? organizationId)
    {
        _auditScope.SetTarget(new AuditTarget(AuditTargetTypes.DataItem, dataItem.Id));
        if (organizationId is Guid itemOrganizationId) _auditScope.SetOrganization(itemOrganizationId);
    }

    // A personal item when no classification is given; a partial or unknown one is rejected;
    // otherwise the owner's clearances decide
    private async Task<Result<DataItem>> CreateDataItem(
        AddDataItemCommand command,
        IFormFile file,
        string content,
        CancellationToken ct)
    {
        if (command.PolicyId is null && command.LevelPriority is null)
            return Result.Success(DataItem.CreatePersonal(file.FileName, file.Length, content, command.OwnerUserId));

        if (command.PolicyId is not Guid policyId || command.LevelPriority is not int priority)
            return Result.Failure<DataItem>(DataItemErrors.IncompleteClassification);

        var level = ClassificationLevel.FindStandard(priority);
        if (level is null) return Result.Failure<DataItem>(DataItemErrors.UnknownClassificationLevel);

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

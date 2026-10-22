using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.DataItems;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features.DataManagement.AddDataItem;
using DataTrustHub.Features.DataManagement.DeleteDataItem;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace DataTrustHub.Features.Tests.DataManagement;

// The command handlers built directly on fake repositories: each command commits exactly once,
// and a rejected command commits nothing (ADR-0012 step 3a)
public class DataItemCommandHandlerTests
{
    private const string FileName = "report.txt";
    private const string FormFieldName = "file";
    private const int Confidential = 2;
    private const int Secret = 3;
    private const int NonStandardPriority = 9;
    private const int NegativePriority = -1;
    private const string IncompleteClassificationCode = "DataItem.IncompleteClassification";
    private const string IncompleteClassificationMessage = "A classified item needs both a policy and a classification level.";
    private const string UnknownClassificationLevelCode = "DataItem.UnknownClassificationLevel";
    private const string UnknownClassificationLevelMessage = "Unknown classification level.";
    private static readonly byte[] FileBytes = "hello world"u8.ToArray();

    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _policyId = Guid.NewGuid();
    private readonly FakeUserRepository _users = new();
    private readonly FakeClearanceRepository _clearances = new();
    private readonly FakeDataItemRepository _dataItems = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task AddDataItem_PersonalUpload_AddsItemAndCommitsOnce()
    {
        _users.ExistingIds.Add(_ownerId);

        var result = await CreateAddHandler().Handle(new AddDataItemCommand(CreateFile(), _ownerId), default);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(_dataItems.Added);
        Assert.Equal(result.Value, item.Id);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task AddDataItem_ClassifiedWithinClearance_AddsItemAndCommitsOnce()
    {
        _users.ExistingIds.Add(_ownerId);
        _clearances.Clearances.Add(Clearance.Grant("c", _ownerId, _policyId, ClassificationLevel.FindStandard(Secret)!));

        var command = new AddDataItemCommand(CreateFile(), _ownerId, _policyId, Confidential);
        var result = await CreateAddHandler().Handle(command, default);

        Assert.True(result.IsSuccess);
        Assert.Single(_dataItems.Added);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task AddDataItem_NotCleared_FailsAndCommitsNothing()
    {
        _users.ExistingIds.Add(_ownerId);
        _clearances.Clearances.Add(Clearance.Grant("c", _ownerId, _policyId, ClassificationLevel.FindStandard(Confidential)!));

        var command = new AddDataItemCommand(CreateFile(), _ownerId, _policyId, Secret);
        var result = await CreateAddHandler().Handle(command, default);

        Assert.Equal(DataItemErrors.NotCleared, result.Error);
        Assert.Empty(_dataItems.Added);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task AddDataItem_NoPolicyAndNoLevel_AddsPersonalItem()
    {
        _users.ExistingIds.Add(_ownerId);

        var command = new AddDataItemCommand(CreateFile(), _ownerId, null, null);
        var result = await CreateAddHandler().Handle(command, default);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(_dataItems.Added);
        Assert.Equal((false, (Guid?)null, (int?)null, DataItem.PersonalMarking),
            (item.IsClassified, item.PolicyId, item.ClassificationPriority, item.SecurityMarking));
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task AddDataItem_PolicyWithoutLevel_FailsWithIncompleteClassificationAndCommitsNothing()
    {
        _users.ExistingIds.Add(_ownerId);
        GrantOwner(Secret);

        var command = new AddDataItemCommand(CreateFile(), _ownerId, _policyId, null);
        var result = await CreateAddHandler().Handle(command, default);

        AssertError(result, IncompleteClassificationCode, IncompleteClassificationMessage);
        Assert.Equal(DataItemErrors.IncompleteClassification, result.Error);
        AssertNothingStaged();
    }

    [Fact]
    public async Task AddDataItem_LevelWithoutPolicy_FailsWithIncompleteClassificationAndCommitsNothing()
    {
        _users.ExistingIds.Add(_ownerId);
        GrantOwner(Secret);

        var command = new AddDataItemCommand(CreateFile(), _ownerId, null, Confidential);
        var result = await CreateAddHandler().Handle(command, default);

        AssertError(result, IncompleteClassificationCode, IncompleteClassificationMessage);
        Assert.Equal(DataItemErrors.IncompleteClassification, result.Error);
        AssertNothingStaged();
    }

    [Fact]
    public async Task AddDataItem_NonStandardLevel_FailsWithUnknownClassificationLevelAndCommitsNothing()
    {
        _users.ExistingIds.Add(_ownerId);
        GrantOwner(Secret);

        var command = new AddDataItemCommand(CreateFile(), _ownerId, _policyId, NonStandardPriority);
        var result = await CreateAddHandler().Handle(command, default);

        AssertError(result, UnknownClassificationLevelCode, UnknownClassificationLevelMessage);
        Assert.Equal(DataItemErrors.UnknownClassificationLevel, result.Error);
        AssertNothingStaged();
    }

    [Fact]
    public async Task AddDataItem_NegativeLevel_FailsWithUnknownClassificationLevelAndCommitsNothing()
    {
        _users.ExistingIds.Add(_ownerId);
        GrantOwner(Secret);

        var command = new AddDataItemCommand(CreateFile(), _ownerId, _policyId, NegativePriority);
        var result = await CreateAddHandler().Handle(command, default);

        Assert.Equal(DataItemErrors.UnknownClassificationLevel, result.Error);
        AssertNothingStaged();
    }

    [Fact]
    public async Task AddDataItem_ClassifiedWithinClearance_StoresPolicyAndLevel()
    {
        _users.ExistingIds.Add(_ownerId);
        GrantOwner(Secret);

        var command = new AddDataItemCommand(CreateFile(), _ownerId, _policyId, Secret);
        var result = await CreateAddHandler().Handle(command, default);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(_dataItems.Added);
        Assert.Equal((true, (Guid?)_policyId, (int?)Secret), (item.IsClassified, item.PolicyId, item.ClassificationPriority));
    }

    [Fact]
    public async Task AddDataItem_ClassifiedWithoutAnyClearance_FailsWithNotClearedAndCommitsNothing()
    {
        _users.ExistingIds.Add(_ownerId);

        var command = new AddDataItemCommand(CreateFile(), _ownerId, _policyId, Confidential);
        var result = await CreateAddHandler().Handle(command, default);

        Assert.Equal(DataItemErrors.NotCleared, result.Error);
        AssertNothingStaged();
    }

    [Fact]
    public async Task AddDataItem_UnknownOwner_FailsAndCommitsNothing()
    {
        var result = await CreateAddHandler().Handle(new AddDataItemCommand(CreateFile(), _ownerId), default);

        Assert.True(result.IsFailure);
        Assert.Empty(_dataItems.Added);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task DeleteDataItem_Owner_MarksDeletedAndCommitsOnce()
    {
        var item = DataItem.CreatePersonal(FileName, FileBytes.Length, Convert.ToBase64String(FileBytes), _ownerId);
        _dataItems.Stored[item.Id] = item;

        var result = await CreateDeleteHandler().Handle(new DeleteDataItemCommand(item.Id, _ownerId), default);

        Assert.True(result.IsSuccess);
        Assert.True(item.IsDeleted);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task DeleteDataItem_OtherUser_ReturnsNotFoundAndCommitsNothing()
    {
        var item = DataItem.CreatePersonal(FileName, FileBytes.Length, Convert.ToBase64String(FileBytes), _ownerId);
        _dataItems.Stored[item.Id] = item;

        var result = await CreateDeleteHandler().Handle(new DeleteDataItemCommand(item.Id, Guid.NewGuid()), default);

        Assert.Equal(DataItemErrors.NotFound, result.Error);
        Assert.False(item.IsDeleted);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task DeleteDataItem_MissingItem_ReturnsNotFoundAndCommitsNothing()
    {
        var result = await CreateDeleteHandler().Handle(new DeleteDataItemCommand(Guid.NewGuid(), _ownerId), default);

        Assert.Equal(DataItemErrors.NotFound, result.Error);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    // --- helpers ---

    private void GrantOwner(int priority) =>
        _clearances.Clearances.Add(Clearance.Grant("c", _ownerId, _policyId, ClassificationLevel.FindStandard(priority)!));

    private void AssertNothingStaged()
    {
        Assert.Empty(_dataItems.Added);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    private static void AssertError<T>(Result<T> result, string expectedCode, string expectedMessage)
    {
        Assert.True(result.IsFailure);
        Assert.Equal((expectedCode, expectedMessage, ErrorType.Validation),
            (result.Error.Code, result.Error.Description, result.Error.Type));
    }

    private AddDataItemHandler CreateAddHandler() => new(_users, _clearances, _dataItems, _unitOfWork);

    private DeleteDataItemHandler CreateDeleteHandler() => new(_dataItems, _unitOfWork);

    private static FormFile CreateFile() =>
        new(new MemoryStream(FileBytes), 0, FileBytes.Length, FormFieldName, FileName);

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public HashSet<Guid> ExistingIds { get; } = [];

        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(ExistingIds.Contains(id));

        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Add(User user) => throw new NotSupportedException();

        public Task UpdateAsync(User user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RemoveAsync(User user, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeClearanceRepository : IClearanceRepository
    {
        public List<Clearance> Clearances { get; } = [];

        public Task<IReadOnlyList<Clearance>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Clearance>>(Clearances.Where(c => c.UserId == userId).ToList());

        public Task<Clearance?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> ExistsAsync(Guid userId, Guid policyId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> AnyForPolicyAsync(Guid policyId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task AddAsync(Clearance clearance, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RemoveAsync(Clearance clearance, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeDataItemRepository : IDataItemRepository
    {
        public List<DataItem> Added { get; } = [];

        public Dictionary<Guid, DataItem> Stored { get; } = [];

        public Task<DataItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Stored.GetValueOrDefault(id));

        public void Add(DataItem dataItem) => Added.Add(dataItem);

        public Task UpdateAsync(DataItem dataItem, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<DataItem>> ListActiveOwnedByAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

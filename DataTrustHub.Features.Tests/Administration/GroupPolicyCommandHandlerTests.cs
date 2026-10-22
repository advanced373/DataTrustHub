using DataTrustHub.Features.Administration.CreateGroup;
using DataTrustHub.Features.Administration.CreatePolicy;
using DataTrustHub.Features.Administration.DeleteGroup;
using DataTrustHub.Features.Administration.DeletePolicy;
using DataTrustHub.Features.Tests.Administration.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

// ADR-0012 step 3b-1: the group and policy commands run on repositories and commit exactly once,
// only on success. The handlers are built directly with fakes, so no database is involved.
public class GroupPolicyCommandHandlerTests
{
    private const string GroupName = "Handler group";
    private const string PolicyName = "Handler policy";

    private readonly FakeOrganizationRepository _organizations = new();
    private readonly FakePolicyRepository _policies = new();
    private readonly FakeClearanceRepository _clearances = new();
    private readonly CountingUnitOfWork _unitOfWork = new();

    // --- CreateGroup ---

    [Fact]
    public async Task CreateGroup_NewName_AddsGroupAndCommitsOnce()
    {
        var result = await CreateGroupHandler().Handle(new CreateGroupCommand(GroupName), default);

        Assert.True(result.IsSuccess);
        var added = Assert.Single(_organizations.Added);
        Assert.Equal((result.Value, GroupName), (added.Id, added.Name));
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task CreateGroup_DuplicateName_FailsWithoutCommit()
    {
        _organizations.Seed(GroupName);

        var result = await CreateGroupHandler().Handle(new CreateGroupCommand(GroupName), default);

        Assert.Equal(AdminErrorCodes.GroupNameAlreadyInUse, result.Error.Code);
        Assert.Empty(_organizations.Added);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // --- CreatePolicy ---

    [Fact]
    public async Task CreatePolicy_NewNameInExistingGroup_AddsPolicyAndCommitsOnce()
    {
        var group = _organizations.Seed(GroupName);

        var result = await CreatePolicyHandler().Handle(new CreatePolicyCommand(PolicyName, group.Id), default);

        Assert.True(result.IsSuccess);
        var added = Assert.Single(_policies.Added);
        Assert.Equal((result.Value, PolicyName, group.Id), (added.Id, added.Name, added.OrganizationId));
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task CreatePolicy_UnknownGroup_FailsWithoutCommit()
    {
        var result = await CreatePolicyHandler().Handle(new CreatePolicyCommand(PolicyName, Guid.NewGuid()), default);

        Assert.Equal(AdminErrorCodes.PolicyOrganizationNotFound, result.Error.Code);
        Assert.Empty(_policies.Added);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // The group is unknown and the name is taken under its id: the group check must win
    [Fact]
    public async Task CreatePolicy_UnknownGroupAndDuplicateName_ReportsUnknownGroupFirst()
    {
        var unknownGroupId = Guid.NewGuid();
        _policies.Seed(PolicyName, unknownGroupId);

        var result = await CreatePolicyHandler().Handle(new CreatePolicyCommand(PolicyName, unknownGroupId), default);

        Assert.Equal(AdminErrorCodes.PolicyOrganizationNotFound, result.Error.Code);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task CreatePolicy_DuplicateNameInSameGroup_FailsWithoutCommit()
    {
        var group = _organizations.Seed(GroupName);
        _policies.Seed(PolicyName, group.Id);

        var result = await CreatePolicyHandler().Handle(new CreatePolicyCommand(PolicyName, group.Id), default);

        Assert.Equal(AdminErrorCodes.PolicyNameAlreadyInUse, result.Error.Code);
        Assert.Empty(_policies.Added);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task CreatePolicy_SameNameInOtherGroup_Succeeds()
    {
        var otherGroup = _organizations.Seed("Other handler group");
        var group = _organizations.Seed(GroupName);
        _policies.Seed(PolicyName, otherGroup.Id);

        var result = await CreatePolicyHandler().Handle(new CreatePolicyCommand(PolicyName, group.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    // --- DeleteGroup ---

    [Fact]
    public async Task DeleteGroup_WithoutPolicies_RemovesGroupAndCommitsOnce()
    {
        var group = _organizations.Seed(GroupName);

        var result = await DeleteGroupHandler().Handle(new DeleteGroupCommand(group.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(group.Id, Assert.Single(_organizations.Removed).Id);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task DeleteGroup_Unknown_FailsWithoutCommit()
    {
        var result = await DeleteGroupHandler().Handle(new DeleteGroupCommand(Guid.NewGuid()), default);

        Assert.Equal(AdminErrorCodes.GroupNotFound, result.Error.Code);
        Assert.Empty(_organizations.Removed);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // The group is unknown and a policy points at its id: the not-found check must win
    [Fact]
    public async Task DeleteGroup_UnknownGroupWithPolicies_ReportsNotFoundFirst()
    {
        var unknownGroupId = Guid.NewGuid();
        _policies.Seed(PolicyName, unknownGroupId);

        var result = await DeleteGroupHandler().Handle(new DeleteGroupCommand(unknownGroupId), default);

        Assert.Equal(AdminErrorCodes.GroupNotFound, result.Error.Code);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task DeleteGroup_WithPolicies_FailsWithoutRemovingOrCommitting()
    {
        var group = _organizations.Seed(GroupName);
        _policies.Seed(PolicyName, group.Id);

        var result = await DeleteGroupHandler().Handle(new DeleteGroupCommand(group.Id), default);

        Assert.Equal(AdminErrorCodes.GroupHasPolicies, result.Error.Code);
        Assert.Empty(_organizations.Removed);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // --- DeletePolicy ---

    [Fact]
    public async Task DeletePolicy_WithoutClearances_RemovesPolicyAndCommitsOnce()
    {
        var policy = _policies.Seed(PolicyName, Guid.NewGuid());

        var result = await DeletePolicyHandler().Handle(new DeletePolicyCommand(policy.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(policy.Id, Assert.Single(_policies.Removed).Id);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task DeletePolicy_Unknown_FailsWithoutCommit()
    {
        var result = await DeletePolicyHandler().Handle(new DeletePolicyCommand(Guid.NewGuid()), default);

        Assert.Equal(AdminErrorCodes.PolicyNotFound, result.Error.Code);
        Assert.Empty(_policies.Removed);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task DeletePolicy_WithClearances_FailsWithoutRemovingOrCommitting()
    {
        var policy = _policies.Seed(PolicyName, Guid.NewGuid());
        _clearances.PoliciesWithClearances.Add(policy.Id);

        var result = await DeletePolicyHandler().Handle(new DeletePolicyCommand(policy.Id), default);

        Assert.Equal(AdminErrorCodes.PolicyHasClearances, result.Error.Code);
        Assert.Empty(_policies.Removed);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // --- helpers ---

    private CreateGroupHandler CreateGroupHandler() => new(_organizations, _unitOfWork);

    private CreatePolicyHandler CreatePolicyHandler() => new(_organizations, _policies, _unitOfWork);

    private DeleteGroupHandler DeleteGroupHandler() => new(_organizations, _policies, _unitOfWork);

    private DeletePolicyHandler DeletePolicyHandler() => new(_policies, _clearances, _unitOfWork);
}

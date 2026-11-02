using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features.Administration.CreateAccount;
using DataTrustHub.Features.Administration.CreateClearance;
using DataTrustHub.Features.Administration.CreateOrganization;
using DataTrustHub.Features.Administration.CreatePolicy;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

// #121 AC1-AC4 (administration use cases): a request passes the use case's uniqueness check while a concurrent
// request with the same values saves first, so the unique index rejects this save. The use case answers with
// its existing conflict error (409), the one its check gives, and does not turn other save failures into it.
// The handlers are built directly with fakes, so no database is involved.
public class UniqueIndexViolationHandlerTests
{
    private const string Email = "Unique.Index@Example.com";
    private const string Password = "Password1!";
    private const string OrganizationName = "Unique index organization";
    private const string PolicyName = "Unique index policy";
    private const string ClearanceName = "Unique index clearance";
    private const string LevelName = "Secret";
    private const int LevelPriority = 2;

    private readonly FakeUserRepository _users = new();
    private readonly FakeOrganizationRepository _organizations = new();
    private readonly FakePolicyRepository _policies = new();
    private readonly FakeClearanceRepository _clearances = new();
    private readonly FakeMembershipRepository _memberships = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly UniqueViolationUnitOfWork _conflictingUnitOfWork = new();

    private static readonly Guid OrganizationId = Guid.NewGuid();
    private readonly Guid _callerId = Guid.NewGuid();

    // --- AC1 (U1): CreateAccount ---

    [Fact]
    public async Task CreateAccount_UniqueIndexRejectsSave_FailsWithEmailAlreadyInUseConflict()
    {
        var result = await new CreateAccountHandler(_users, _hasher, _conflictingUnitOfWork)
            .Handle(new CreateAccountCommand(Email, Password), default);

        AssertConflict(result, AdminErrorCodes.AccountEmailAlreadyInUse, AdminErrorMessages.AccountEmailAlreadyInUse);
        Assert.Equal(1, _conflictingUnitOfWork.SaveChangesCalls);
    }

    // The same error as the check gives for an email in use, so a client cannot tell the two paths apart
    [Fact]
    public async Task CreateAccount_UniqueIndexRejectsSave_FailsWithTheSameErrorAsTheCheck()
    {
        var fromIndex = await new CreateAccountHandler(_users, _hasher, _conflictingUnitOfWork)
            .Handle(new CreateAccountCommand(Email, Password), default);
        _users.Seed(Email);
        var fromCheck = await new CreateAccountHandler(_users, _hasher, new CountingUnitOfWork())
            .Handle(new CreateAccountCommand(Email, Password), default);

        Assert.Equal(fromCheck.Error, fromIndex.Error);
    }

    [Fact]
    public async Task CreateAccount_OtherSaveFailure_IsNotTurnedIntoAConflict()
    {
        var handler = new CreateAccountHandler(_users, _hasher, new FailingUnitOfWork());

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new CreateAccountCommand(Email, Password), default));
    }

    // --- AC2 (O1): CreateOrganization ---

    [Fact]
    public async Task CreateOrganization_UniqueIndexRejectsSave_FailsWithOrganizationNameAlreadyInUseConflict()
    {
        var result = await new CreateOrganizationHandler(_organizations, _conflictingUnitOfWork)
            .Handle(new CreateOrganizationCommand(OrganizationName), default);

        AssertConflict(result, AdminErrorCodes.OrganizationNameAlreadyInUse, AdminErrorMessages.OrganizationNameAlreadyInUse);
        Assert.Equal(1, _conflictingUnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task CreateOrganization_UniqueIndexRejectsSave_FailsWithTheSameErrorAsTheCheck()
    {
        var fromIndex = await new CreateOrganizationHandler(_organizations, _conflictingUnitOfWork)
            .Handle(new CreateOrganizationCommand(OrganizationName), default);
        _organizations.Seed(OrganizationName);
        var fromCheck = await new CreateOrganizationHandler(_organizations, new CountingUnitOfWork())
            .Handle(new CreateOrganizationCommand(OrganizationName), default);

        Assert.Equal(fromCheck.Error, fromIndex.Error);
    }

    [Fact]
    public async Task CreateOrganization_OtherSaveFailure_IsNotTurnedIntoAConflict()
    {
        var handler = new CreateOrganizationHandler(_organizations, new FailingUnitOfWork());

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new CreateOrganizationCommand(OrganizationName), default));
    }

    // --- AC3 (P2): CreatePolicy ---

    [Fact]
    public async Task CreatePolicy_UniqueIndexRejectsSave_FailsWithPolicyNameAlreadyInUseConflict()
    {
        var result = await CreatePolicyHandler(_conflictingUnitOfWork).Handle(PolicyCommand(), default);

        AssertConflict(result, AdminErrorCodes.PolicyNameAlreadyInUse, AdminErrorMessages.PolicyNameAlreadyInUse);
        Assert.Equal(1, _conflictingUnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task CreatePolicy_UniqueIndexRejectsSave_FailsWithTheSameErrorAsTheCheck()
    {
        var fromIndex = await CreatePolicyHandler(_conflictingUnitOfWork).Handle(PolicyCommand(), default);
        _policies.Seed(PolicyName, OrganizationId);
        var fromCheck = await CreatePolicyHandler(new CountingUnitOfWork()).Handle(PolicyCommand(), default);

        Assert.Equal(fromCheck.Error, fromIndex.Error);
    }

    [Fact]
    public async Task CreatePolicy_OtherSaveFailure_IsNotTurnedIntoAConflict()
    {
        var handler = CreatePolicyHandler(new FailingUnitOfWork());

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(PolicyCommand(), default));
    }

    // --- AC4 (C3): CreateClearance ---

    [Fact]
    public async Task CreateClearance_UniqueIndexRejectsSave_FailsWithAlreadyGrantedConflict()
    {
        var (userId, policyId) = SeedMemberAndPolicy();

        var result = await CreateClearanceHandler(_conflictingUnitOfWork).Handle(ClearanceCommand(userId, policyId), default);

        AssertConflict(result, AdminErrorCodes.ClearanceAlreadyGranted, AdminErrorMessages.ClearanceAlreadyGranted);
        Assert.Equal(1, _conflictingUnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task CreateClearance_UniqueIndexRejectsSave_FailsWithTheSameErrorAsTheCheck()
    {
        var (userId, policyId) = SeedMemberAndPolicy();

        var fromIndex = await CreateClearanceHandler(_conflictingUnitOfWork).Handle(ClearanceCommand(userId, policyId), default);
        _clearances.Seed(userId, policyId);
        var fromCheck = await CreateClearanceHandler(new CountingUnitOfWork()).Handle(ClearanceCommand(userId, policyId), default);

        Assert.Equal(fromCheck.Error, fromIndex.Error);
    }

    [Fact]
    public async Task CreateClearance_OtherSaveFailure_IsNotTurnedIntoAConflict()
    {
        var (userId, policyId) = SeedMemberAndPolicy();
        var handler = CreateClearanceHandler(new FailingUnitOfWork());

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(ClearanceCommand(userId, policyId), default));
    }

    // --- helpers ---

    private static void AssertConflict(Result<Guid> result, string expectedCode, string expectedMessage)
    {
        Assert.True(result.IsFailure);
        Assert.Equal((ErrorType.Conflict, expectedCode, expectedMessage), (result.Error.Type, result.Error.Code, result.Error.Description));
    }

    private CreatePolicyCommand PolicyCommand() => new(OrganizationId, _callerId, PolicyName);

    private CreatePolicyHandler CreatePolicyHandler(IUnitOfWork unitOfWork)
    {
        SeedCallerAsOrgAdmin();
        return new(_memberships, _policies, unitOfWork);
    }

    private CreateClearanceCommand ClearanceCommand(Guid userId, Guid policyId) =>
        new(OrganizationId, _callerId, userId, policyId, ClearanceName, LevelName, LevelPriority);

    private CreateClearanceHandler CreateClearanceHandler(IUnitOfWork unitOfWork)
    {
        SeedCallerAsOrgAdmin();
        return new(_memberships, _users, _policies, _clearances, unitOfWork);
    }

    // A registered member of the organization (C5) and a policy of the organization
    private (Guid UserId, Guid PolicyId) SeedMemberAndPolicy()
    {
        var user = _users.Seed(Email);
        _memberships.Seed(Membership.AddMember(user.Id, OrganizationId));
        return (user.Id, _policies.Seed(PolicyName, OrganizationId).Id);
    }

    private void SeedCallerAsOrgAdmin()
    {
        if (!_memberships.Stored.Any(m => m.UserId == _callerId))
            _memberships.Seed(Membership.AppointOrgAdmin(_callerId, OrganizationId));
    }
}

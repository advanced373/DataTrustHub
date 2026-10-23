using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features.Administration.CreateOrganization;
using DataTrustHub.Features.Administration.CreatePolicy;
using DataTrustHub.Features.Administration.DeleteOrganization;
using DataTrustHub.Features.Administration.DeletePolicy;
using DataTrustHub.Features.Tests.Administration.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

// ADR-0012 step 3b-1: the organization and policy commands run on repositories and commit exactly once,
// only on success. The handlers are built directly with fakes, so no database is involved.
public class OrganizationPolicyCommandHandlerTests
{
    private const string OrganizationName = "Handler organization";
    private const string PolicyName = "Handler policy";

    private readonly FakeOrganizationRepository _organizations = new();
    private readonly FakePolicyRepository _policies = new();
    private readonly FakeClearanceRepository _clearances = new();
    private readonly FakeMembershipRepository _memberships = new();
    private readonly CountingUnitOfWork _unitOfWork = new();

    // --- CreateOrganization ---

    [Fact]
    public async Task CreateOrganization_NewName_AddsOrganizationAndCommitsOnce()
    {
        var result = await CreateOrganizationHandler().Handle(new CreateOrganizationCommand(OrganizationName), default);

        Assert.True(result.IsSuccess);
        var added = Assert.Single(_organizations.Added);
        Assert.Equal((result.Value, OrganizationName), (added.Id, added.Name));
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task CreateOrganization_DuplicateName_FailsWithoutCommit()
    {
        _organizations.Seed(OrganizationName);

        var result = await CreateOrganizationHandler().Handle(new CreateOrganizationCommand(OrganizationName), default);

        Assert.Equal(AdminErrorCodes.OrganizationNameAlreadyInUse, result.Error.Code);
        Assert.Empty(_organizations.Added);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // --- CreatePolicy ---

    [Fact]
    public async Task CreatePolicy_NewNameInExistingOrganization_AddsPolicyAndCommitsOnce()
    {
        var organization = _organizations.Seed(OrganizationName);

        var result = await CreatePolicyHandler().Handle(new CreatePolicyCommand(PolicyName, organization.Id), default);

        Assert.True(result.IsSuccess);
        var added = Assert.Single(_policies.Added);
        Assert.Equal((result.Value, PolicyName, organization.Id), (added.Id, added.Name, added.OrganizationId));
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task CreatePolicy_UnknownOrganization_FailsWithoutCommit()
    {
        var result = await CreatePolicyHandler().Handle(new CreatePolicyCommand(PolicyName, Guid.NewGuid()), default);

        Assert.Equal(AdminErrorCodes.PolicyOrganizationNotFound, result.Error.Code);
        Assert.Empty(_policies.Added);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // The organization is unknown and the name is taken under its id: the organization check must win
    [Fact]
    public async Task CreatePolicy_UnknownOrganizationAndDuplicateName_ReportsUnknownOrganizationFirst()
    {
        var unknownOrganizationId = Guid.NewGuid();
        _policies.Seed(PolicyName, unknownOrganizationId);

        var result = await CreatePolicyHandler().Handle(new CreatePolicyCommand(PolicyName, unknownOrganizationId), default);

        Assert.Equal(AdminErrorCodes.PolicyOrganizationNotFound, result.Error.Code);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task CreatePolicy_DuplicateNameInSameOrganization_FailsWithoutCommit()
    {
        var organization = _organizations.Seed(OrganizationName);
        _policies.Seed(PolicyName, organization.Id);

        var result = await CreatePolicyHandler().Handle(new CreatePolicyCommand(PolicyName, organization.Id), default);

        Assert.Equal(AdminErrorCodes.PolicyNameAlreadyInUse, result.Error.Code);
        Assert.Empty(_policies.Added);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task CreatePolicy_SameNameInOtherOrganization_Succeeds()
    {
        var otherOrganization = _organizations.Seed("Other handler organization");
        var organization = _organizations.Seed(OrganizationName);
        _policies.Seed(PolicyName, otherOrganization.Id);

        var result = await CreatePolicyHandler().Handle(new CreatePolicyCommand(PolicyName, organization.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    // --- DeleteOrganization ---

    [Fact]
    public async Task DeleteOrganization_WithoutPolicies_RemovesOrganizationAndCommitsOnce()
    {
        var organization = _organizations.Seed(OrganizationName);

        var result = await DeleteOrganizationHandler().Handle(new DeleteOrganizationCommand(organization.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(organization.Id, Assert.Single(_organizations.Removed).Id);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    // AC6 of #95 step 2b: the organization's memberships end with it, in the same save; others stay
    [Fact]
    public async Task DeleteOrganization_WithMemberships_RemovesThemInTheSameCommit()
    {
        var organization = _organizations.Seed(OrganizationName);
        var orgAdmin = _memberships.Seed(Membership.AppointOrgAdmin(Guid.NewGuid(), organization.Id));
        var member = _memberships.Seed(Membership.AddMember(Guid.NewGuid(), organization.Id));
        var guest = _memberships.Seed(Membership.AddGuest(Guid.NewGuid(), organization.Id));
        var otherOrganizationMembership = _memberships.Seed(Membership.AddMember(member.UserId, Guid.NewGuid()));

        var result = await DeleteOrganizationHandler().Handle(new DeleteOrganizationCommand(organization.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new HashSet<Guid> { orgAdmin.Id, member.Id, guest.Id },
            _memberships.Removed.Select(m => m.Id).ToHashSet());
        Assert.DoesNotContain(otherOrganizationMembership, _memberships.Removed);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task DeleteOrganization_WithPoliciesAndMemberships_KeepsMembershipsWithoutCommit()
    {
        var organization = _organizations.Seed(OrganizationName);
        _policies.Seed(PolicyName, organization.Id);
        _memberships.Seed(Membership.AppointOrgAdmin(Guid.NewGuid(), organization.Id));

        var result = await DeleteOrganizationHandler().Handle(new DeleteOrganizationCommand(organization.Id), default);

        Assert.Equal(AdminErrorCodes.OrganizationHasPolicies, result.Error.Code);
        Assert.Empty(_memberships.Removed);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task DeleteOrganization_Unknown_FailsWithoutCommit()
    {
        var result = await DeleteOrganizationHandler().Handle(new DeleteOrganizationCommand(Guid.NewGuid()), default);

        Assert.Equal(AdminErrorCodes.OrganizationNotFound, result.Error.Code);
        Assert.Empty(_organizations.Removed);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // The organization is unknown and a policy points at its id: the not-found check must win
    [Fact]
    public async Task DeleteOrganization_UnknownOrganizationWithPolicies_ReportsNotFoundFirst()
    {
        var unknownOrganizationId = Guid.NewGuid();
        _policies.Seed(PolicyName, unknownOrganizationId);

        var result = await DeleteOrganizationHandler().Handle(new DeleteOrganizationCommand(unknownOrganizationId), default);

        Assert.Equal(AdminErrorCodes.OrganizationNotFound, result.Error.Code);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task DeleteOrganization_WithPolicies_FailsWithoutRemovingOrCommitting()
    {
        var organization = _organizations.Seed(OrganizationName);
        _policies.Seed(PolicyName, organization.Id);

        var result = await DeleteOrganizationHandler().Handle(new DeleteOrganizationCommand(organization.Id), default);

        Assert.Equal(AdminErrorCodes.OrganizationHasPolicies, result.Error.Code);
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

    private CreateOrganizationHandler CreateOrganizationHandler() => new(_organizations, _unitOfWork);

    private CreatePolicyHandler CreatePolicyHandler() => new(_organizations, _policies, _unitOfWork);

    private DeleteOrganizationHandler DeleteOrganizationHandler() => new(_organizations, _policies, _memberships, _unitOfWork);

    private DeletePolicyHandler DeletePolicyHandler() => new(_policies, _clearances, _unitOfWork);
}

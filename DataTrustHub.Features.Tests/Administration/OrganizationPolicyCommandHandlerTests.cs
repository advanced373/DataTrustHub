using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features.Administration.CreateOrganization;
using DataTrustHub.Features.Administration.CreatePolicy;
using DataTrustHub.Features.Administration.DeleteOrganization;
using DataTrustHub.Features.Administration.DeletePolicy;
using DataTrustHub.Features.Tests.Administration.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

// ADR-0012 step 3b-1: the organization and policy commands run on repositories and commit exactly once,
// only on success. Since #95 step 3a the policy commands first check that the caller is an organization admin. The handlers are built directly with fakes, so no database is involved.
public class OrganizationPolicyCommandHandlerTests
{
    private const string OrganizationName = "Handler organization";
    private const string PolicyName = "Handler policy";
    private const string MemberRole = "Member";
    private const string GuestRole = "Guest";

    public static TheoryData<string> NotOrgAdminRoles => new(MemberRole, GuestRole);

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

    // --- CreatePolicy (#95 step 3a: an organization admin of the organization, C6) ---

    [Fact]
    public async Task CreatePolicy_OrgAdminNewName_AddsPolicyAndCommitsOnce()
    {
        var organization = _organizations.Seed(OrganizationName);
        var callerId = SeedOrgAdmin(organization.Id);

        var result = await CreatePolicyHandler().Handle(new CreatePolicyCommand(organization.Id, callerId, PolicyName), default);

        Assert.True(result.IsSuccess);
        var added = Assert.Single(_policies.Added);
        Assert.Equal((result.Value, PolicyName, organization.Id), (added.Id, added.Name, added.OrganizationId));
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Theory]
    [MemberData(nameof(NotOrgAdminRoles))]
    public async Task CreatePolicy_CallerNotOrgAdmin_FailsWithOrganizationNotFoundWithoutCommit(string role)
    {
        var organization = _organizations.Seed(OrganizationName);
        var callerId = SeedMembership(role, organization.Id);

        var result = await CreatePolicyHandler().Handle(new CreatePolicyCommand(organization.Id, callerId, PolicyName), default);

        Assert.Equal(AdminErrorCodes.OrganizationNotFound, result.Error.Code);
        Assert.Empty(_policies.Added);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task CreatePolicy_OrgAdminOfAnotherOrganization_FailsWithOrganizationNotFoundWithoutCommit()
    {
        var organization = _organizations.Seed(OrganizationName);
        var callerId = SeedOrgAdmin(_organizations.Seed("Other handler organization").Id);

        var result = await CreatePolicyHandler().Handle(new CreatePolicyCommand(organization.Id, callerId, PolicyName), default);

        Assert.Equal(AdminErrorCodes.OrganizationNotFound, result.Error.Code);
        Assert.Empty(_policies.Added);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task CreatePolicy_UnknownOrganization_FailsWithOrganizationNotFoundWithoutCommit()
    {
        var result = await CreatePolicyHandler().Handle(
            new CreatePolicyCommand(Guid.NewGuid(), Guid.NewGuid(), PolicyName), default);

        Assert.Equal(AdminErrorCodes.OrganizationNotFound, result.Error.Code);
        Assert.Empty(_policies.Added);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // The caller is not an organization admin and the name is taken: the caller check must win, so P2 reveals nothing
    [Fact]
    public async Task CreatePolicy_CallerNotOrgAdminAndDuplicateName_ReportsOrganizationNotFoundFirst()
    {
        var organization = _organizations.Seed(OrganizationName);
        _policies.Seed(PolicyName, organization.Id);

        var result = await CreatePolicyHandler().Handle(
            new CreatePolicyCommand(organization.Id, Guid.NewGuid(), PolicyName), default);

        Assert.Equal(AdminErrorCodes.OrganizationNotFound, result.Error.Code);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task CreatePolicy_DuplicateNameInSameOrganization_FailsWithoutCommit()
    {
        var organization = _organizations.Seed(OrganizationName);
        var callerId = SeedOrgAdmin(organization.Id);
        _policies.Seed(PolicyName, organization.Id);

        var result = await CreatePolicyHandler().Handle(new CreatePolicyCommand(organization.Id, callerId, PolicyName), default);

        Assert.Equal(AdminErrorCodes.PolicyNameAlreadyInUse, result.Error.Code);
        Assert.Empty(_policies.Added);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task CreatePolicy_SameNameInOtherOrganization_Succeeds()
    {
        var otherOrganization = _organizations.Seed("Other handler organization");
        var organization = _organizations.Seed(OrganizationName);
        var callerId = SeedOrgAdmin(organization.Id);
        _policies.Seed(PolicyName, otherOrganization.Id);

        var result = await CreatePolicyHandler().Handle(new CreatePolicyCommand(organization.Id, callerId, PolicyName), default);

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

    // --- DeletePolicy (#95 step 3a: an organization admin of the policy's organization, C6) ---

    [Fact]
    public async Task DeletePolicy_OrgAdminWithoutClearances_RemovesPolicyAndCommitsOnce()
    {
        var organizationId = Guid.NewGuid();
        var callerId = SeedOrgAdmin(organizationId);
        var policy = _policies.Seed(PolicyName, organizationId);

        var result = await DeletePolicyHandler().Handle(new DeletePolicyCommand(organizationId, callerId, policy.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(policy.Id, Assert.Single(_policies.Removed).Id);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task DeletePolicy_Unknown_FailsWithoutCommit()
    {
        var organizationId = Guid.NewGuid();
        var callerId = SeedOrgAdmin(organizationId);

        var result = await DeletePolicyHandler().Handle(new DeletePolicyCommand(organizationId, callerId, Guid.NewGuid()), default);

        Assert.Equal(AdminErrorCodes.PolicyNotFound, result.Error.Code);
        Assert.Empty(_policies.Removed);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // A policy of another organization is not found, so its existence is not revealed
    [Fact]
    public async Task DeletePolicy_PolicyOfAnotherOrganization_FailsWithPolicyNotFoundWithoutCommit()
    {
        var organizationId = Guid.NewGuid();
        var callerId = SeedOrgAdmin(organizationId);
        var otherPolicy = _policies.Seed(PolicyName, Guid.NewGuid());

        var result = await DeletePolicyHandler().Handle(new DeletePolicyCommand(organizationId, callerId, otherPolicy.Id), default);

        Assert.Equal(AdminErrorCodes.PolicyNotFound, result.Error.Code);
        Assert.Empty(_policies.Removed);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // A 409 from P3 would reveal the other organization's policy, so the scope check must come first
    [Fact]
    public async Task DeletePolicy_PolicyOfAnotherOrganizationWithClearances_ReportsPolicyNotFoundFirst()
    {
        var organizationId = Guid.NewGuid();
        var callerId = SeedOrgAdmin(organizationId);
        var otherPolicy = _policies.Seed(PolicyName, Guid.NewGuid());
        _clearances.PoliciesWithClearances.Add(otherPolicy.Id);

        var result = await DeletePolicyHandler().Handle(new DeletePolicyCommand(organizationId, callerId, otherPolicy.Id), default);

        Assert.Equal(AdminErrorCodes.PolicyNotFound, result.Error.Code);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Theory]
    [MemberData(nameof(NotOrgAdminRoles))]
    public async Task DeletePolicy_CallerNotOrgAdmin_FailsWithOrganizationNotFoundWithoutRemoving(string role)
    {
        var organizationId = Guid.NewGuid();
        var callerId = SeedMembership(role, organizationId);
        var policy = _policies.Seed(PolicyName, organizationId);

        var result = await DeletePolicyHandler().Handle(new DeletePolicyCommand(organizationId, callerId, policy.Id), default);

        Assert.Equal(AdminErrorCodes.OrganizationNotFound, result.Error.Code);
        Assert.Empty(_policies.Removed);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task DeletePolicy_CallerWithoutMembership_FailsWithOrganizationNotFoundWithoutRemoving()
    {
        var organizationId = Guid.NewGuid();
        var policy = _policies.Seed(PolicyName, organizationId);

        var result = await DeletePolicyHandler().Handle(new DeletePolicyCommand(organizationId, Guid.NewGuid(), policy.Id), default);

        Assert.Equal(AdminErrorCodes.OrganizationNotFound, result.Error.Code);
        Assert.Empty(_policies.Removed);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task DeletePolicy_WithClearances_FailsWithoutRemovingOrCommitting()
    {
        var organizationId = Guid.NewGuid();
        var callerId = SeedOrgAdmin(organizationId);
        var policy = _policies.Seed(PolicyName, organizationId);
        _clearances.PoliciesWithClearances.Add(policy.Id);

        var result = await DeletePolicyHandler().Handle(new DeletePolicyCommand(organizationId, callerId, policy.Id), default);

        Assert.Equal(AdminErrorCodes.PolicyHasClearances, result.Error.Code);
        Assert.Empty(_policies.Removed);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // --- helpers ---

    private CreateOrganizationHandler CreateOrganizationHandler() => new(_organizations, _unitOfWork);

    private CreatePolicyHandler CreatePolicyHandler() => new(_memberships, _policies, _unitOfWork);

    private DeleteOrganizationHandler DeleteOrganizationHandler() => new(_organizations, _policies, _memberships, _unitOfWork);

    private DeletePolicyHandler DeletePolicyHandler() => new(_memberships, _policies, _clearances, _unitOfWork);

    private Guid SeedOrgAdmin(Guid organizationId) =>
        _memberships.Seed(Membership.AppointOrgAdmin(Guid.NewGuid(), organizationId)).UserId;

    private Guid SeedMembership(string role, Guid organizationId) =>
        _memberships.Seed(role == GuestRole
            ? Membership.AddGuest(Guid.NewGuid(), organizationId)
            : Membership.AddMember(Guid.NewGuid(), organizationId)).UserId;
}

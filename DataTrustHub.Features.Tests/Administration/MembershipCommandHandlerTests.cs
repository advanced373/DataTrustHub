using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features.Administration.AddMember;
using DataTrustHub.Features.Administration.AppointOrganizationAdmin;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

// #95 step 2a (ADR-0014): appoint an organization admin and add a member, built directly with fakes, so no
// database is involved. Covers the save count and M1 under concurrency (the unique index rejects the save)
public class MembershipCommandHandlerTests
{
    private const string MemberRole = "Member";
    private const string GuestRole = "Guest";
    private const string OrganizationName = "Membership handler organization";

    // The caller of AppointOrganizationAdmin, unless a test appoints the caller themselves
    private static readonly Guid PlatformAdminId = Guid.NewGuid();

    private readonly FakeOrganizationRepository _organizations = new();
    private readonly FakeUserRepository _users = new();
    private readonly FakeMembershipRepository _memberships = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly UniqueViolationUnitOfWork _conflictingUnitOfWork = new();

    // --- AppointOrganizationAdmin (the platform admin appoints the first organization admin, never themselves) ---

    [Fact]
    public async Task AppointOrganizationAdmin_UserNotAMember_AddsOrgAdminMembershipAndCommitsOnce()
    {
        var (organizationId, userId) = SeedOrganizationAndUser();

        var result = await AppointHandler().Handle(AppointCommand(organizationId, userId), default);

        Assert.True(result.IsSuccess);
        var added = Assert.Single(_memberships.Added);
        Assert.Equal((result.Value, userId, organizationId, MembershipRole.OrgAdmin), (added.Id, added.UserId, added.OrganizationId, added.Role));
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task AppointOrganizationAdmin_Member_ChangesRoleToOrgAdminAndCommitsOnce()
    {
        var (organizationId, userId) = SeedOrganizationAndUser();
        var member = _memberships.Seed(Membership.AddMember(userId, organizationId));

        var result = await AppointHandler().Handle(AppointCommand(organizationId, userId), default);

        Assert.Equal(member.Id, result.Value);
        Assert.Equal(MembershipRole.OrgAdmin, member.Role);
        Assert.Equal(member.Id, Assert.Single(_memberships.Updated).Id);
        Assert.Empty(_memberships.Added);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task AppointOrganizationAdmin_Guest_FailsWithGuestRoleIsFixedWithoutCommit()
    {
        var (organizationId, userId) = SeedOrganizationAndUser();
        var guest = _memberships.Seed(Membership.AddGuest(userId, organizationId));

        var result = await AppointHandler().Handle(AppointCommand(organizationId, userId), default);

        AssertAppointFailedWithoutCommit(result, AdminErrorCodes.MembershipGuestRoleIsFixed, ErrorType.Conflict);
        Assert.Equal(MembershipRole.Guest, guest.Role);
    }

    // Replaces the former idempotent answer: the user is already the organization admin
    [Fact]
    public async Task AppointOrganizationAdmin_UserAlreadyOrgAdmin_FailsWithAlreadyHasOrgAdmin()
    {
        var (organizationId, userId) = SeedOrganizationAndUser();
        _memberships.Seed(Membership.AppointOrgAdmin(userId, organizationId));

        var result = await AppointHandler().Handle(AppointCommand(organizationId, userId), default);

        AssertAppointFailedWithoutCommit(result, AdminErrorCodes.OrganizationAlreadyHasOrgAdmin, ErrorType.Conflict);
    }

    // Further organization admins are appointed by the organization's own admins (step 2b)
    [Fact]
    public async Task AppointOrganizationAdmin_OrganizationHasAnotherOrgAdmin_FailsWithAlreadyHasOrgAdmin()
    {
        var (organizationId, userId) = SeedOrganizationAndUser();
        _memberships.Seed(Membership.AppointOrgAdmin(Guid.NewGuid(), organizationId));
        var member = _memberships.Seed(Membership.AddMember(userId, organizationId));

        var result = await AppointHandler().Handle(AppointCommand(organizationId, userId), default);

        AssertAppointFailedWithoutCommit(result, AdminErrorCodes.OrganizationAlreadyHasOrgAdmin, ErrorType.Conflict);
        Assert.Equal(MembershipRole.Member, member.Role);
    }

    [Fact]
    public async Task AppointOrganizationAdmin_CallerAppointsThemselves_FailsWithCannotAppointSelf()
    {
        var organizationId = _organizations.Seed(OrganizationName).Id;
        var platformAdminId = _users.SeedAdmin("appoint.self@example.com").Id;

        var result = await AppointHandler().Handle(SelfCommand(organizationId, platformAdminId), default);

        AssertAppointFailedWithoutCommit(result, AdminErrorCodes.MembershipCannotAppointSelf, ErrorType.Conflict);
    }

    // Order of checks: self comes before "already has an admin"
    [Fact]
    public async Task AppointOrganizationAdmin_SelfInOrganizationWithOrgAdmin_FailsWithCannotAppointSelf()
    {
        var organizationId = _organizations.Seed(OrganizationName).Id;
        var platformAdminId = _users.SeedAdmin("appoint.self.admin@example.com").Id;
        _memberships.Seed(Membership.AppointOrgAdmin(Guid.NewGuid(), organizationId));

        var result = await AppointHandler().Handle(SelfCommand(organizationId, platformAdminId), default);

        AssertAppointFailedWithoutCommit(result, AdminErrorCodes.MembershipCannotAppointSelf, ErrorType.Conflict);
    }

    // Order of checks: the organization comes before self
    [Fact]
    public async Task AppointOrganizationAdmin_UnknownOrganizationAndSelf_FailsWithOrganizationNotFound()
    {
        var platformAdminId = _users.SeedAdmin("appoint.unknown.org@example.com").Id;

        var result = await AppointHandler().Handle(SelfCommand(Guid.NewGuid(), platformAdminId), default);

        AssertAppointFailedWithoutCommit(result, AdminErrorCodes.OrganizationNotFound, ErrorType.NotFound);
    }

    // Order of checks: the user comes before "already has an admin"
    [Fact]
    public async Task AppointOrganizationAdmin_UnknownUserInOrganizationWithOrgAdmin_FailsWithUserNotFound()
    {
        var organizationId = _organizations.Seed(OrganizationName).Id;
        _memberships.Seed(Membership.AppointOrgAdmin(Guid.NewGuid(), organizationId));

        var result = await AppointHandler().Handle(AppointCommand(organizationId, Guid.NewGuid()), default);

        AssertAppointFailedWithoutCommit(result, AdminErrorCodes.MembershipUserNotFound, ErrorType.NotFound);
    }

    // Order of checks: the user comes before self (the caller's own account no longer exists)
    [Fact]
    public async Task AppointOrganizationAdmin_SelfWhoseAccountIsGone_FailsWithUserNotFound()
    {
        var organizationId = _organizations.Seed(OrganizationName).Id;

        var result = await AppointHandler().Handle(SelfCommand(organizationId, Guid.NewGuid()), default);

        AssertAppointFailedWithoutCommit(result, AdminErrorCodes.MembershipUserNotFound, ErrorType.NotFound);
    }

    // M1 under concurrency: another request created the membership after GetAsync and before the save
    [Fact]
    public async Task AppointOrganizationAdmin_UniqueIndexRejectsSave_FailsWithAlreadyMember()
    {
        var (organizationId, userId) = SeedOrganizationAndUser();
        var handler = new AppointOrganizationAdminHandler(_organizations, _users, _memberships, _conflictingUnitOfWork);

        var result = await handler.Handle(AppointCommand(organizationId, userId), default);

        Assert.Equal((AdminErrorCodes.MembershipAlreadyMember, ErrorType.Conflict), (result.Error.Code, result.Error.Type));
        Assert.Equal(1, _conflictingUnitOfWork.SaveChangesCalls);
    }

    // --- AddMember ---

    [Theory]
    [InlineData(MemberRole)]
    [InlineData(GuestRole)]
    public async Task AddMember_CallerIsOrgAdmin_AddsMembershipWithRoleAndCommitsOnce(string role)
    {
        var (organizationId, callerId, userId) = SeedOrgAdminAndUser();

        var result = await AddMemberHandler().Handle(new AddMemberCommand(organizationId, callerId, userId, role), default);

        Assert.True(result.IsSuccess);
        var added = Assert.Single(_memberships.Added);
        Assert.Equal((result.Value, userId, organizationId, role), (added.Id, added.UserId, added.OrganizationId, added.Role.Name));
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task AddMember_CallerWithoutMembership_FailsWithOrganizationNotFound()
    {
        var (organizationId, _, userId) = SeedOrgAdminAndUser();

        var result = await AddMemberHandler().Handle(new AddMemberCommand(organizationId, Guid.NewGuid(), userId, MemberRole), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.OrganizationNotFound, ErrorType.NotFound);
    }

    [Theory]
    [InlineData(MemberRole)]
    [InlineData(GuestRole)]
    public async Task AddMember_CallerIsMemberOrGuest_FailsWithOrganizationNotFound(string callerRole)
    {
        var (organizationId, _, userId) = SeedOrgAdminAndUser();
        var callerId = _users.Seed("add.member.caller@example.com").Id;
        _memberships.Seed(callerRole == GuestRole
            ? Membership.AddGuest(callerId, organizationId)
            : Membership.AddMember(callerId, organizationId));

        var result = await AddMemberHandler().Handle(new AddMemberCommand(organizationId, callerId, userId, MemberRole), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.OrganizationNotFound, ErrorType.NotFound);
    }

    [Fact]
    public async Task AddMember_CallerIsOrgAdminOfAnotherOrganization_FailsWithOrganizationNotFound()
    {
        var (_, callerId, userId) = SeedOrgAdminAndUser();
        var otherOrganizationId = _organizations.Seed("Other organization").Id;

        var result = await AddMemberHandler().Handle(new AddMemberCommand(otherOrganizationId, callerId, userId, MemberRole), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.OrganizationNotFound, ErrorType.NotFound);
    }

    // The caller check comes first, so a caller outside the organization learns nothing about the user
    [Fact]
    public async Task AddMember_CallerNotOrgAdminAndUnknownUser_FailsWithOrganizationNotFound()
    {
        var organizationId = _organizations.Seed(OrganizationName).Id;

        var result = await AddMemberHandler().Handle(new AddMemberCommand(organizationId, Guid.NewGuid(), Guid.NewGuid(), MemberRole), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.OrganizationNotFound, ErrorType.NotFound);
    }

    [Fact]
    public async Task AddMember_UnknownUser_FailsWithUserNotFound()
    {
        var (organizationId, callerId, _) = SeedOrgAdminAndUser();

        var result = await AddMemberHandler().Handle(new AddMemberCommand(organizationId, callerId, Guid.NewGuid(), MemberRole), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.MembershipUserNotFound, ErrorType.NotFound);
    }

    // Invariant M1, checked with ExistsAsync before the save
    [Fact]
    public async Task AddMember_UserAlreadyMember_FailsWithAlreadyMemberWithoutCommit()
    {
        var (organizationId, callerId, userId) = SeedOrgAdminAndUser();
        _memberships.Seed(Membership.AddGuest(userId, organizationId));

        var result = await AddMemberHandler().Handle(new AddMemberCommand(organizationId, callerId, userId, MemberRole), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.MembershipAlreadyMember, ErrorType.Conflict);
    }

    // M1 under concurrency: both requests passed ExistsAsync, the second save hits the unique index
    [Fact]
    public async Task AddMember_UniqueIndexRejectsSave_FailsWithAlreadyMember()
    {
        var (organizationId, callerId, userId) = SeedOrgAdminAndUser();
        var handler = new AddMemberHandler(_users, _memberships, _conflictingUnitOfWork);

        var result = await handler.Handle(new AddMemberCommand(organizationId, callerId, userId, GuestRole), default);

        Assert.Equal((AdminErrorCodes.MembershipAlreadyMember, ErrorType.Conflict), (result.Error.Code, result.Error.Type));
        Assert.Equal(1, _conflictingUnitOfWork.SaveChangesCalls);
    }

    // --- helpers ---

    private (Guid OrganizationId, Guid UserId) SeedOrganizationAndUser() =>
        (_organizations.Seed(OrganizationName).Id, _users.Seed("membership.handler.user@example.com").Id);

    private (Guid OrganizationId, Guid CallerId, Guid UserId) SeedOrgAdminAndUser()
    {
        var (organizationId, userId) = SeedOrganizationAndUser();
        var callerId = _users.Seed("membership.handler.orgadmin@example.com").Id;
        _memberships.Seed(Membership.AppointOrgAdmin(callerId, organizationId));
        return (organizationId, callerId, userId);
    }

    private static AppointOrganizationAdminCommand AppointCommand(Guid organizationId, Guid userId) =>
        new(organizationId, PlatformAdminId, userId);

    private static AppointOrganizationAdminCommand SelfCommand(Guid organizationId, Guid callerId) =>
        new(organizationId, callerId, callerId);

    private void AssertAppointFailedWithoutCommit(Result<Guid> result, string expectedCode, ErrorType expectedType)
    {
        AssertFailedWithoutCommit(result, expectedCode, expectedType);
        Assert.Empty(_memberships.Updated);
    }

    private void AssertFailedWithoutCommit(Result<Guid> result, string expectedCode, ErrorType expectedType)
    {
        Assert.Equal((expectedCode, expectedType), (result.Error.Code, result.Error.Type));
        Assert.Empty(_memberships.Added);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    private AppointOrganizationAdminHandler AppointHandler() => new(_organizations, _users, _memberships, _unitOfWork);

    private AddMemberHandler AddMemberHandler() => new(_users, _memberships, _unitOfWork);
}

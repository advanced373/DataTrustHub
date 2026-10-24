using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features.Administration.AddMember;
using DataTrustHub.Features.Administration.AppointOrganizationAdmin;
using DataTrustHub.Features.Administration.ChangeMemberRole;
using DataTrustHub.Features.Administration.CreateClearance;
using DataTrustHub.Features.Administration.CreatePolicy;
using DataTrustHub.Features.Administration.DeleteAccount;
using DataTrustHub.Features.Administration.DeleteClearance;
using DataTrustHub.Features.Administration.DeleteOrganization;
using DataTrustHub.Features.Administration.DeletePolicy;
using DataTrustHub.Features.Administration.LeaveOrganization;
using DataTrustHub.Features.Administration.RemoveMember;
using DataTrustHub.Features.Authentication.LoginUser;
using DataTrustHub.Features.Authentication.RegisterUser;
using DataTrustHub.Features.DataManagement.DeleteDataItem;
using Xunit;

namespace DataTrustHub.Features.Tests.Auditing;

// AC6 of #24 step 1: the commands declare their organization and their target for the audit trail
public class AuditedCommandContractTests
{
    private const string Role = "Member";
    private const string Name = "Audit contract";
    private const string LevelName = "Secret";
    private const int LevelPriority = 3;
    private const string Email = "audit_contract@example.com";
    private const string Password = "Password1!";

    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();
    private static readonly Guid SubjectId = Guid.NewGuid();
    private static readonly Guid OtherId = Guid.NewGuid();

    // Every organization-scoped command, built with OrganizationId
    public static TheoryData<object> OrganizationScopedCommands => new()
    {
        new AddMemberCommand(OrganizationId, CallerId, SubjectId, Role),
        new AppointOrganizationAdminCommand(OrganizationId, CallerId, SubjectId),
        new ChangeMemberRoleCommand(OrganizationId, CallerId, SubjectId, Role),
        new CreateClearanceCommand(OrganizationId, CallerId, SubjectId, OtherId, Name, LevelName, LevelPriority),
        new CreatePolicyCommand(OrganizationId, CallerId, Name),
        new DeleteClearanceCommand(OrganizationId, CallerId, SubjectId),
        new DeleteOrganizationCommand(OrganizationId),
        new DeletePolicyCommand(OrganizationId, CallerId, SubjectId),
        new LeaveOrganizationCommand(OrganizationId, CallerId),
        new RemoveMemberCommand(OrganizationId, CallerId, SubjectId)
    };

    // Every targeted command with the target type and id it must declare
    public static TheoryData<object, string, Guid> TargetedCommands => new()
    {
        { new DeleteDataItemCommand(SubjectId, CallerId), AuditTargetTypes.DataItem, SubjectId },
        { new DeleteClearanceCommand(OrganizationId, CallerId, SubjectId), AuditTargetTypes.Clearance, SubjectId },
        { new DeletePolicyCommand(OrganizationId, CallerId, SubjectId), AuditTargetTypes.Policy, SubjectId },
        { new DeleteOrganizationCommand(OrganizationId), AuditTargetTypes.Organization, OrganizationId },
        { new DeleteAccountCommand(SubjectId, CallerId), AuditTargetTypes.User, SubjectId },
        { new AddMemberCommand(OrganizationId, CallerId, SubjectId, Role), AuditTargetTypes.User, SubjectId },
        { new AppointOrganizationAdminCommand(OrganizationId, CallerId, SubjectId), AuditTargetTypes.User, SubjectId },
        { new ChangeMemberRoleCommand(OrganizationId, CallerId, SubjectId, Role), AuditTargetTypes.User, SubjectId },
        { new RemoveMemberCommand(OrganizationId, CallerId, SubjectId), AuditTargetTypes.User, SubjectId }
    };

    [Theory]
    [MemberData(nameof(OrganizationScopedCommands))]
    public void OrganizationScopedCommand_DeclaresItsOrganization(object command)
    {
        var scoped = Assert.IsAssignableFrom<IOrganizationScopedCommand>(command);

        Assert.Equal(OrganizationId, scoped.OrganizationId);
    }

    [Theory]
    [MemberData(nameof(TargetedCommands))]
    public void TargetedCommand_DeclaresItsTarget(object command, string targetType, Guid targetId)
    {
        var targeted = Assert.IsAssignableFrom<ITargetedCommand>(command);

        Assert.Equal(new AuditTarget(targetType, targetId), targeted.Target);
    }

    [Fact]
    public void AuditTargetTypes_AreOneNamePerAggregate()
    {
        string[] expected = ["User", "Organization", "Policy", "Clearance", "DataItem"];

        string[] actual =
            [AuditTargetTypes.User, AuditTargetTypes.Organization, AuditTargetTypes.Policy, AuditTargetTypes.Clearance, AuditTargetTypes.DataItem];

        Assert.Equal(expected, actual);
    }

    // AC7: the anonymous commands declare nothing, so nothing of theirs reaches the record
    [Fact]
    public void RegisterAndLogin_DeclareNoOrganizationAndNoTarget()
    {
        object[] commands = [new RegisterUserCommand(Email, Password), new LoginUserCommand(Email, Password)];

        Assert.All(commands, command =>
        {
            Assert.IsNotAssignableFrom<IOrganizationScopedCommand>(command);
            Assert.IsNotAssignableFrom<ITargetedCommand>(command);
        });
    }
}

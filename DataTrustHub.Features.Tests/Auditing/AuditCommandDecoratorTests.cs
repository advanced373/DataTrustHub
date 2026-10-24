using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using DataTrustHub.Domain.Auditing;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features.DataManagement.DeleteDataItem;
using DataTrustHub.Features.Tests.Auditing.Helpers;
using DataTrustHub.SharedKernel;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DataTrustHub.Features.Tests.Auditing;

// AC5 of #24 step 1 (ADR-0016 points 1, 5): the audit decorator records every command with its outcome,
// its actor, its organization and its target, and an audit write that fails changes nothing for the caller
public class AuditCommandDecoratorTests
{
    private const string ProbeAction = "Probe";
    private const string PlainProbeAction = "PlainProbe";
    private const string DeleteDataItemAction = "DeleteDataItem";
    private const string RejectedCode = "Probe.Rejected";
    private const string RejectedDescription = "The probe was rejected.";
    private const string ValidationCode = "Validation.General";
    private const string FieldCode = "Probe.FieldRequired";

    private readonly AuditDecoratorHarness _harness = new();
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _organizationId = Guid.NewGuid();
    private readonly Guid _targetId = Guid.NewGuid();

    // --- outcome ---

    [Fact]
    public async Task Handle_InnerSucceeds_RecordsSucceededWithoutErrorCode()
    {
        var result = await Decorate(_ => Task.FromResult(Result.Success())).Handle(new PlainProbeCommand(), default);

        Assert.True(result.IsSuccess);
        var record = Assert.Single(_harness.AuditLog.Appended);
        Assert.Equal((AuditOutcome.Succeeded, (string?)null), (record.Outcome, record.ErrorCode));
    }

    [Fact]
    public async Task Handle_InnerRejectsWithDomainError_RecordsRejectedWithTheErrorCode()
    {
        var error = Error.NotFound(RejectedCode, RejectedDescription);

        await Decorate(_ => Task.FromResult(Result.Failure(error))).Handle(new PlainProbeCommand(), default);

        var record = Assert.Single(_harness.AuditLog.Appended);
        Assert.Equal((AuditOutcome.Rejected, (string?)RejectedCode), (record.Outcome, record.ErrorCode));
    }

    [Fact]
    public async Task Handle_InnerRejectsWithValidationError_RecordsRejectedWithTheValidationCode()
    {
        var error = new ValidationError([Error.Problem(FieldCode, RejectedDescription)]);

        await Decorate(_ => Task.FromResult(Result.Failure(error))).Handle(new PlainProbeCommand(), default);

        var record = Assert.Single(_harness.AuditLog.Appended);
        Assert.Equal((AuditOutcome.Rejected, (string?)ValidationCode), (record.Outcome, record.ErrorCode));
    }

    [Fact]
    public async Task Handle_GenericResultSucceeds_RecordsSucceededAndReturnsTheValue()
    {
        var value = Guid.NewGuid();
        var handler = _harness.Decorate<PlainProbeCommand, Result<Guid>>(_ => Task.FromResult(Result.Success(value)));

        var result = await handler.Handle(new PlainProbeCommand(), default);

        Assert.Equal(value, result.Value);
        Assert.Equal(AuditOutcome.Succeeded, Assert.Single(_harness.AuditLog.Appended).Outcome);
    }

    [Fact]
    public async Task Handle_GenericResultRejected_RecordsRejectedWithTheErrorCode()
    {
        var error = Error.Conflict(RejectedCode, RejectedDescription);
        var handler = _harness.Decorate<PlainProbeCommand, Result<Guid>>(_ => Task.FromResult(Result.Failure<Guid>(error)));

        await handler.Handle(new PlainProbeCommand(), default);

        var record = Assert.Single(_harness.AuditLog.Appended);
        Assert.Equal((AuditOutcome.Rejected, (string?)RejectedCode), (record.Outcome, record.ErrorCode));
    }

    [Fact]
    public async Task Handle_InnerResult_IsReturnedUnchanged()
    {
        var expected = Result.Failure(Error.Problem(RejectedCode, RejectedDescription));

        var result = await Decorate(_ => Task.FromResult(expected)).Handle(new PlainProbeCommand(), default);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task Handle_InnerThrows_RecordsFailedWithTheExceptionTypeName()
    {
        await Assert.ThrowsAsync<ProbeException>(() =>
            Decorate(_ => throw new ProbeException()).Handle(new PlainProbeCommand(), default));

        var record = Assert.Single(_harness.AuditLog.Appended);
        Assert.Equal((AuditOutcome.Failed, (string?)nameof(ProbeException)), (record.Outcome, record.ErrorCode));
    }

    [Fact]
    public async Task Handle_InnerThrows_RethrowsTheSameException()
    {
        var expected = new ProbeException();

        var thrown = await Assert.ThrowsAsync<ProbeException>(() =>
            Decorate(_ => throw expected).Handle(new PlainProbeCommand(), default));

        Assert.Same(expected, thrown);
    }

    [Fact]
    public async Task Handle_InnerThrowsAsynchronously_RecordsFailedAndRethrowsTheSameException()
    {
        var expected = new ProbeException();

        var thrown = await Assert.ThrowsAsync<ProbeException>(() =>
            Decorate(async _ => { await Task.Yield(); throw expected; }).Handle(new PlainProbeCommand(), default));

        Assert.Same(expected, thrown);
        Assert.Equal(AuditOutcome.Failed, Assert.Single(_harness.AuditLog.Appended).Outcome);
    }

    // --- action ---

    [Fact]
    public async Task Handle_Action_IsTheCommandTypeNameWithoutTheCommandSuffix()
    {
        await Decorate(_ => Task.FromResult(Result.Success())).Handle(new PlainProbeCommand(), default);

        Assert.Equal(PlainProbeAction, Assert.Single(_harness.AuditLog.Appended).Action);
    }

    [Fact]
    public async Task Handle_RealCommand_ActionIsDeleteDataItem()
    {
        var handler = _harness.Decorate<DeleteDataItemCommand, Result<Guid>>(c => Task.FromResult(Result.Success(c.DataItemId)));

        await handler.Handle(new DeleteDataItemCommand(_targetId, _actorUserId), default);

        Assert.Equal(DeleteDataItemAction, Assert.Single(_harness.AuditLog.Appended).Action);
    }

    // --- actor ---

    [Fact]
    public async Task Handle_AuthenticatedWithSubClaim_RecordsTheActor()
    {
        _harness.SignIn(new Claim(JwtRegisteredClaimNames.Sub, _actorUserId.ToString()));

        await Decorate(_ => Task.FromResult(Result.Success())).Handle(new PlainProbeCommand(), default);

        Assert.Equal(_actorUserId, Assert.Single(_harness.AuditLog.Appended).ActorUserId);
    }

    [Fact]
    public async Task Handle_NoRequest_RecordsAnonymous()
    {
        await Decorate(_ => Task.FromResult(Result.Success())).Handle(new PlainProbeCommand(), default);

        Assert.Null(Assert.Single(_harness.AuditLog.Appended).ActorUserId);
    }

    [Fact]
    public async Task Handle_UnauthenticatedRequest_RecordsAnonymousEvenWithASubClaim()
    {
        _harness.SignInAnonymous(new Claim(JwtRegisteredClaimNames.Sub, _actorUserId.ToString()));

        await Decorate(_ => Task.FromResult(Result.Success())).Handle(new PlainProbeCommand(), default);

        Assert.Null(Assert.Single(_harness.AuditLog.Appended).ActorUserId);
    }

    [Fact]
    public async Task Handle_InnerThrows_StillRecordsTheActor()
    {
        _harness.SignIn(new Claim(JwtRegisteredClaimNames.Sub, _actorUserId.ToString()));

        await Assert.ThrowsAsync<ProbeException>(() =>
            Decorate(_ => throw new ProbeException()).Handle(new PlainProbeCommand(), default));

        Assert.Equal(_actorUserId, Assert.Single(_harness.AuditLog.Appended).ActorUserId);
    }

    // --- organization and target ---

    [Fact]
    public async Task Handle_PlainCommand_RecordsNoOrganizationAndNoTarget()
    {
        await Decorate(_ => Task.FromResult(Result.Success())).Handle(new PlainProbeCommand(), default);

        var record = Assert.Single(_harness.AuditLog.Appended);
        Assert.Equal(((Guid?)null, (string?)null, (Guid?)null), (record.OrganizationId, record.TargetType, record.TargetId));
    }

    [Fact]
    public async Task Handle_ScopedAndTargetedCommand_RecordsItsOrganizationAndTarget()
    {
        var handler = _harness.Decorate<ProbeCommand, Result>(_ => Task.FromResult(Result.Success()));

        await handler.Handle(new ProbeCommand(_organizationId, _targetId), default);

        var record = Assert.Single(_harness.AuditLog.Appended);
        Assert.Equal(ProbeAction, record.Action);
        Assert.Equal(((Guid?)_organizationId, (string?)AuditTargetTypes.Policy, (Guid?)_targetId),
            (record.OrganizationId, record.TargetType, record.TargetId));
    }

    [Fact]
    public async Task Handle_RejectedScopedCommand_StillRecordsItsOrganizationAndTarget()
    {
        var error = Error.NotFound(RejectedCode, RejectedDescription);
        var handler = _harness.Decorate<ProbeCommand, Result>(_ => Task.FromResult(Result.Failure(error)));

        await handler.Handle(new ProbeCommand(_organizationId, _targetId), default);

        var record = Assert.Single(_harness.AuditLog.Appended);
        Assert.Equal(((Guid?)_organizationId, (Guid?)_targetId), (record.OrganizationId, record.TargetId));
    }

    [Fact]
    public async Task Handle_HandlerFillsAuditScope_ScopeOverridesTheCommandsOrganizationAndTarget()
    {
        var scopeOrganizationId = Guid.NewGuid();
        var scopeTarget = new AuditTarget(AuditTargetTypes.DataItem, Guid.NewGuid());
        var handler = _harness.Decorate<ProbeCommand, Result>(_ =>
        {
            _harness.AuditScope.SetOrganization(scopeOrganizationId);
            _harness.AuditScope.SetTarget(scopeTarget);
            return Task.FromResult(Result.Success());
        });

        await handler.Handle(new ProbeCommand(_organizationId, _targetId), default);

        var record = Assert.Single(_harness.AuditLog.Appended);
        Assert.Equal(((Guid?)scopeOrganizationId, (string?)scopeTarget.Type, (Guid?)scopeTarget.Id),
            (record.OrganizationId, record.TargetType, record.TargetId));
    }

    [Fact]
    public async Task Handle_HandlerFillsAuditScopeForAPlainCommand_RecordsTheScopesValues()
    {
        var scopeTarget = new AuditTarget(AuditTargetTypes.DataItem, _targetId);
        var handler = Decorate(_ =>
        {
            _harness.AuditScope.SetOrganization(_organizationId);
            _harness.AuditScope.SetTarget(scopeTarget);
            return Task.FromResult(Result.Success());
        });

        await handler.Handle(new PlainProbeCommand(), default);

        var record = Assert.Single(_harness.AuditLog.Appended);
        Assert.Equal(((Guid?)_organizationId, (string?)AuditTargetTypes.DataItem, (Guid?)_targetId),
            (record.OrganizationId, record.TargetType, record.TargetId));
    }

    [Fact]
    public async Task Handle_HandlerFillsOnlyTheScopesOrganization_KeepsTheCommandsTarget()
    {
        var scopeOrganizationId = Guid.NewGuid();
        var handler = _harness.Decorate<ProbeCommand, Result>(_ =>
        {
            _harness.AuditScope.SetOrganization(scopeOrganizationId);
            return Task.FromResult(Result.Success());
        });

        await handler.Handle(new ProbeCommand(_organizationId, _targetId), default);

        var record = Assert.Single(_harness.AuditLog.Appended);
        Assert.Equal(((Guid?)scopeOrganizationId, (Guid?)_targetId), (record.OrganizationId, record.TargetId));
    }

    // --- an audit write that fails ---

    [Fact]
    public async Task Handle_AuditWriteFails_ReturnsTheInnerResultUnchanged()
    {
        _harness.AuditLog.ThrowOnAppend = new InvalidOperationException();
        var expected = Result.Success();

        var result = await Decorate(_ => Task.FromResult(expected)).Handle(new PlainProbeCommand(), default);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task Handle_AuditWriteFailsAfterAnInnerException_RethrowsTheInnerException()
    {
        _harness.AuditLog.ThrowOnAppend = new InvalidOperationException();
        var expected = new ProbeException();

        var thrown = await Assert.ThrowsAsync<ProbeException>(() =>
            Decorate(_ => throw expected).Handle(new PlainProbeCommand(), default));

        Assert.Same(expected, thrown);
    }

    [Fact]
    public async Task Handle_AuditWriteFails_LogsOneErrorNamingTheActionAndOutcome()
    {
        _harness.AuditLog.ThrowOnAppend = new InvalidOperationException();

        await Decorate(_ => Task.FromResult(Result.Success())).Handle(new PlainProbeCommand(), default);

        var entry = Assert.Single(_harness.Logs.Entries, e => e.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains(PlainProbeAction, entry.Message);
        Assert.Contains(nameof(AuditOutcome.Succeeded), entry.Message);
    }

    [Fact]
    public async Task Handle_AuditWriteFails_LogsNoneOfTheRecordsOtherData()
    {
        _harness.SignIn(new Claim(JwtRegisteredClaimNames.Sub, _actorUserId.ToString()));
        _harness.AuditLog.ThrowOnAppend = new InvalidOperationException(_organizationId.ToString());
        var error = Error.NotFound(RejectedCode, RejectedDescription);
        var handler = _harness.Decorate<ProbeCommand, Result>(_ => Task.FromResult(Result.Failure(error)));

        await handler.Handle(new ProbeCommand(_organizationId, _targetId), default);

        var entry = Assert.Single(_harness.Logs.Entries);
        string[] privateValues = [_actorUserId.ToString(), _organizationId.ToString(), _targetId.ToString(), RejectedCode];
        Assert.All(privateValues, value => Assert.DoesNotContain(value, entry.Message));
        Assert.All(privateValues, value => Assert.DoesNotContain(value, entry.Exception?.ToString() ?? string.Empty));
    }

    [Fact]
    public async Task Handle_AuditWriteSucceeds_LogsNoError()
    {
        await Decorate(_ => Task.FromResult(Result.Success())).Handle(new PlainProbeCommand(), default);

        Assert.DoesNotContain(_harness.Logs.Entries, e => e.Level >= LogLevel.Error);
    }

    // --- helpers ---

    private Features._Shared.Messaging.ICommandHandler<PlainProbeCommand, Result> Decorate(
        Func<PlainProbeCommand, Task<Result>> inner) => _harness.Decorate(inner);

    // A command that declares neither an organization nor a target
    public sealed record PlainProbeCommand;

    // A command that declares both, like DeletePolicyCommand
    public sealed record ProbeCommand(Guid OrganizationId, Guid PolicyId) : IOrganizationScopedCommand, ITargetedCommand
    {
        AuditTarget ITargetedCommand.Target => new(AuditTargetTypes.Policy, PolicyId);
    }

    private sealed class ProbeException : Exception;
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using DataTrustHub.Domain.Auditing;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.FindUserByEmail;
using DataTrustHub.Features.Tests.Auditing.Helpers;
using DataTrustHub.SharedKernel;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DataTrustHub.Features.Tests.Auditing;

// #122 Part B (ADR-0025): the query audit decorator records a query that opts in with IAuditedQuery like the
// command decorator records a command (AC4), leaves any other query alone (AC5), and an audit write that fails
// changes nothing for the caller (AC6)
public class AuditQueryDecoratorTests
{
    private const string AuditedProbeAction = "AuditedProbe";
    private const string ScopedProbeAction = "ScopedProbe";
    private const string FindUserByEmailAction = "FindUserByEmail";
    private const string RejectedCode = "Probe.Rejected";
    private const string RejectedDescription = "The probe was rejected.";
    private const string ValidationCode = "Validation.General";
    private const string FieldCode = "Probe.FieldRequired";
    private const string SearchedEmail = "probe_searched@example.com";

    private readonly AuditDecoratorHarness _harness = new();
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _organizationId = Guid.NewGuid();
    private readonly Guid _targetId = Guid.NewGuid();

    // --- AC5: a query without IAuditedQuery ---

    [Fact]
    public async Task Handle_QueryNotAudited_AppendsNoRecord()
    {
        var handler = _harness.DecorateQuery<PlainProbeQuery, Result>(_ => Task.FromResult(Result.Success()));

        await handler.Handle(new PlainProbeQuery(), default);

        Assert.Empty(_harness.AuditLog.Appended);
    }

    [Fact]
    public async Task Handle_QueryNotAuditedAndRejected_AppendsNoRecord()
    {
        var error = Error.NotFound(RejectedCode, RejectedDescription);
        var handler = _harness.DecorateQuery<PlainProbeQuery, Result>(_ => Task.FromResult(Result.Failure(error)));

        await handler.Handle(new PlainProbeQuery(), default);

        Assert.Empty(_harness.AuditLog.Appended);
    }

    [Fact]
    public async Task Handle_QueryNotAuditedAndThrows_AppendsNoRecordAndRethrows()
    {
        var expected = new ProbeException();
        var handler = _harness.DecorateQuery<PlainProbeQuery, Result>(_ => throw expected);

        var thrown = await Assert.ThrowsAsync<ProbeException>(() => handler.Handle(new PlainProbeQuery(), default));

        Assert.Same(expected, thrown);
        Assert.Empty(_harness.AuditLog.Appended);
    }

    [Fact]
    public async Task Handle_QueryNotAudited_ReturnsTheInnerResultUnchanged()
    {
        var expected = Result.Success(Guid.NewGuid());
        var handler = _harness.DecorateQuery<PlainProbeQuery, Result<Guid>>(_ => Task.FromResult(expected));

        var result = await handler.Handle(new PlainProbeQuery(), default);

        Assert.Same(expected, result);
    }

    // A query that declares an organization but does not opt in is still not audited
    [Fact]
    public async Task Handle_OrganizationScopedQueryWithoutIAuditedQuery_AppendsNoRecord()
    {
        var handler = _harness.DecorateQuery<ScopedOnlyProbeQuery, Result>(_ => Task.FromResult(Result.Success()));

        await handler.Handle(new ScopedOnlyProbeQuery(_organizationId), default);

        Assert.Empty(_harness.AuditLog.Appended);
    }

    // --- AC4: outcome of an audited query ---

    [Fact]
    public async Task Handle_AuditedQuerySucceeds_RecordsOneSucceededWithoutErrorCode()
    {
        var handler = _harness.DecorateQuery<AuditedProbeQuery, Result>(_ => Task.FromResult(Result.Success()));

        await handler.Handle(new AuditedProbeQuery(), default);

        var record = Assert.Single(_harness.AuditLog.Appended);
        Assert.Equal((AuditOutcome.Succeeded, (string?)null), (record.Outcome, record.ErrorCode));
    }

    [Fact]
    public async Task Handle_AuditedQueryRejected_RecordsOneRejectedWithTheErrorCode()
    {
        var error = Error.NotFound(RejectedCode, RejectedDescription);
        var handler = _harness.DecorateQuery<AuditedProbeQuery, Result<Guid>>(_ => Task.FromResult(Result.Failure<Guid>(error)));

        await handler.Handle(new AuditedProbeQuery(), default);

        var record = Assert.Single(_harness.AuditLog.Appended);
        Assert.Equal((AuditOutcome.Rejected, (string?)RejectedCode), (record.Outcome, record.ErrorCode));
    }

    [Fact]
    public async Task Handle_AuditedQueryRejectedByValidation_RecordsRejectedWithTheValidationCode()
    {
        var error = new ValidationError([Error.Problem(FieldCode, RejectedDescription)]);
        var handler = _harness.DecorateQuery<AuditedProbeQuery, Result>(_ => Task.FromResult(Result.Failure(error)));

        await handler.Handle(new AuditedProbeQuery(), default);

        var record = Assert.Single(_harness.AuditLog.Appended);
        Assert.Equal((AuditOutcome.Rejected, (string?)ValidationCode), (record.Outcome, record.ErrorCode));
    }

    [Fact]
    public async Task Handle_AuditedQueryThrows_RecordsFailedWithTheExceptionTypeNameAndRethrows()
    {
        var expected = new ProbeException();
        var handler = _harness.DecorateQuery<AuditedProbeQuery, Result>(_ => throw expected);

        var thrown = await Assert.ThrowsAsync<ProbeException>(() => handler.Handle(new AuditedProbeQuery(), default));

        Assert.Same(expected, thrown);
        var record = Assert.Single(_harness.AuditLog.Appended);
        Assert.Equal((AuditOutcome.Failed, (string?)nameof(ProbeException)), (record.Outcome, record.ErrorCode));
    }

    [Fact]
    public async Task Handle_AuditedQuery_ReturnsTheInnerResultUnchanged()
    {
        var expected = Result.Failure(Error.Problem(RejectedCode, RejectedDescription));
        var handler = _harness.DecorateQuery<AuditedProbeQuery, Result>(_ => Task.FromResult(expected));

        var result = await handler.Handle(new AuditedProbeQuery(), default);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task Handle_AuditedQuery_AppendsWithoutTheRequestsToken()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = _harness.DecorateQuery<AuditedProbeQuery, Result>(_ => Task.FromResult(Result.Success()));

        await handler.Handle(new AuditedProbeQuery(), cancellation.Token);

        Assert.Equal(CancellationToken.None, Assert.Single(_harness.AuditLog.Tokens));
    }

    // --- AC4: action, actor, organization, target ---

    [Fact]
    public async Task Handle_Action_IsTheQueryTypeNameWithoutTheQuerySuffix()
    {
        var handler = _harness.DecorateQuery<AuditedProbeQuery, Result>(_ => Task.FromResult(Result.Success()));

        await handler.Handle(new AuditedProbeQuery(), default);

        Assert.Equal(AuditedProbeAction, Assert.Single(_harness.AuditLog.Appended).Action);
    }

    [Fact]
    public async Task Handle_RealFindUserByEmailQuery_IsAuditedWithActionFindUserByEmail()
    {
        var handler = _harness.DecorateQuery<FindUserByEmailQuery, Result<FindUserByEmailResponse>>(_ =>
            Task.FromResult(Result.Failure<FindUserByEmailResponse>(Error.NotFound(RejectedCode, RejectedDescription))));

        await handler.Handle(new FindUserByEmailQuery(_organizationId, _actorUserId, SearchedEmail), default);

        var record = Assert.Single(_harness.AuditLog.Appended);
        Assert.Equal((FindUserByEmailAction, (Guid?)_organizationId), (record.Action, record.OrganizationId));
    }

    [Fact]
    public async Task Handle_AuthenticatedCaller_RecordsTheActor()
    {
        _harness.SignIn(new Claim(JwtRegisteredClaimNames.Sub, _actorUserId.ToString()));
        var handler = _harness.DecorateQuery<AuditedProbeQuery, Result>(_ => Task.FromResult(Result.Success()));

        await handler.Handle(new AuditedProbeQuery(), default);

        Assert.Equal(_actorUserId, Assert.Single(_harness.AuditLog.Appended).ActorUserId);
    }

    [Fact]
    public async Task Handle_OrganizationScopedQuery_RecordsItsOrganizationAndNoTarget()
    {
        var handler = _harness.DecorateQuery<ScopedProbeQuery, Result>(_ => Task.FromResult(Result.Success()));

        await handler.Handle(new ScopedProbeQuery(_organizationId), default);

        var record = Assert.Single(_harness.AuditLog.Appended);
        Assert.Equal(ScopedProbeAction, record.Action);
        Assert.Equal(((Guid?)_organizationId, (string?)null, (Guid?)null), (record.OrganizationId, record.TargetType, record.TargetId));
    }

    [Fact]
    public async Task Handle_QueryNotOrganizationScoped_RecordsNoOrganization()
    {
        var handler = _harness.DecorateQuery<AuditedProbeQuery, Result>(_ => Task.FromResult(Result.Success()));

        await handler.Handle(new AuditedProbeQuery(), default);

        Assert.Null(Assert.Single(_harness.AuditLog.Appended).OrganizationId);
    }

    [Fact]
    public async Task Handle_HandlerSetsTarget_RecordsTheTarget()
    {
        var handler = _harness.DecorateQuery<ScopedProbeQuery, Result>(_ =>
        {
            _harness.AuditScope.SetTarget(new AuditTarget(AuditTargetTypes.User, _targetId));
            return Task.FromResult(Result.Success());
        });

        await handler.Handle(new ScopedProbeQuery(_organizationId), default);

        var record = Assert.Single(_harness.AuditLog.Appended);
        Assert.Equal(((string?)AuditTargetTypes.User, (Guid?)_targetId), (record.TargetType, record.TargetId));
    }

    [Fact]
    public async Task Handle_HandlerSetsOrganization_ScopeWinsOverTheQuerysOrganization()
    {
        var scopeOrganizationId = Guid.NewGuid();
        var handler = _harness.DecorateQuery<ScopedProbeQuery, Result>(_ =>
        {
            _harness.AuditScope.SetOrganization(scopeOrganizationId);
            return Task.FromResult(Result.Success());
        });

        await handler.Handle(new ScopedProbeQuery(_organizationId), default);

        Assert.Equal(scopeOrganizationId, Assert.Single(_harness.AuditLog.Appended).OrganizationId);
    }

    // --- AC6: an audit write that fails ---

    [Fact]
    public async Task Handle_AuditWriteFails_ReturnsTheInnerResultUnchanged()
    {
        _harness.AuditLog.ThrowOnAppend = new InvalidOperationException();
        var expected = Result.Success(Guid.NewGuid());
        var handler = _harness.DecorateQuery<AuditedProbeQuery, Result<Guid>>(_ => Task.FromResult(expected));

        var result = await handler.Handle(new AuditedProbeQuery(), default);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task Handle_AuditWriteFailsOnARejection_ReturnsTheRejectionUnchanged()
    {
        _harness.AuditLog.ThrowOnAppend = new InvalidOperationException();
        var expected = Result.Failure(Error.NotFound(RejectedCode, RejectedDescription));
        var handler = _harness.DecorateQuery<AuditedProbeQuery, Result>(_ => Task.FromResult(expected));

        var result = await handler.Handle(new AuditedProbeQuery(), default);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task Handle_AuditWriteFailsAfterAnInnerException_RethrowsTheInnerException()
    {
        _harness.AuditLog.ThrowOnAppend = new InvalidOperationException();
        var expected = new ProbeException();
        var handler = _harness.DecorateQuery<AuditedProbeQuery, Result>(_ => throw expected);

        var thrown = await Assert.ThrowsAsync<ProbeException>(() => handler.Handle(new AuditedProbeQuery(), default));

        Assert.Same(expected, thrown);
    }

    [Fact]
    public async Task Handle_AuditWriteFails_LogsOneErrorNamingTheActionAndOutcome()
    {
        _harness.AuditLog.ThrowOnAppend = new InvalidOperationException();
        var handler = _harness.DecorateQuery<AuditedProbeQuery, Result>(_ => Task.FromResult(Result.Success()));

        await handler.Handle(new AuditedProbeQuery(), default);

        var entry = Assert.Single(_harness.Logs.Entries, e => e.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains(AuditedProbeAction, entry.Message);
        Assert.Contains(nameof(AuditOutcome.Succeeded), entry.Message);
    }

    // --- AC7: the email searched for ---

    [Fact]
    public async Task Handle_FindUserByEmailAuditWriteFails_LogsNoEmail()
    {
        _harness.SignIn(new Claim(JwtRegisteredClaimNames.Sub, _actorUserId.ToString()));
        _harness.AuditLog.ThrowOnAppend = new InvalidOperationException(SearchedEmail);
        var handler = _harness.DecorateQuery<FindUserByEmailQuery, Result<FindUserByEmailResponse>>(_ =>
            Task.FromResult(Result.Failure<FindUserByEmailResponse>(Error.NotFound(RejectedCode, RejectedDescription))));

        await handler.Handle(new FindUserByEmailQuery(_organizationId, _actorUserId, SearchedEmail), default);

        var entry = Assert.Single(_harness.Logs.Entries);
        Assert.DoesNotContain(SearchedEmail, entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SearchedEmail, entry.Exception?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_FindUserByEmailQuery_RecordHoldsNoEmail()
    {
        var handler = _harness.DecorateQuery<FindUserByEmailQuery, Result<FindUserByEmailResponse>>(_ =>
            Task.FromResult(Result.Failure<FindUserByEmailResponse>(Error.NotFound(RejectedCode, RejectedDescription))));

        await handler.Handle(new FindUserByEmailQuery(_organizationId, _actorUserId, SearchedEmail), default);

        var record = Assert.Single(_harness.AuditLog.Appended);
        string?[] texts = [record.Action, record.TargetType, record.ErrorCode];
        Assert.All(texts, text => Assert.DoesNotContain(SearchedEmail, text ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    // --- helpers ---

    // A query that does not opt in to auditing
    public sealed record PlainProbeQuery;

    // A query that declares an organization but does not opt in to auditing
    public sealed record ScopedOnlyProbeQuery(Guid OrganizationId) : IOrganizationScopedQuery;

    // An audited query that declares no organization
    public sealed record AuditedProbeQuery : IAuditedQuery;

    // An audited query inside one organization, like FindUserByEmailQuery
    public sealed record ScopedProbeQuery(Guid OrganizationId) : IAuditedQuery, IOrganizationScopedQuery;

    private sealed class ProbeException : Exception;
}

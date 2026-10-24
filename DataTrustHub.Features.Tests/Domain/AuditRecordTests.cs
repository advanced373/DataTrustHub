using DataTrustHub.Domain.Auditing;
using Xunit;

namespace DataTrustHub.Features.Tests.Domain;

// AC1 of #24 step 1 (ADR-0016 point 3): the audit record is created through a factory that enforces its
// rules, and it holds only who, what, on which target, in which organization, with which outcome
public class AuditRecordTests
{
    private const string Action = "DeleteDataItem";
    private const string TargetType = "DataItem";
    private const string ErrorCode = "DataItem.NotFound";

    private static readonly DateTime UtcNow = DateTime.UtcNow;
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly Guid TargetId = Guid.NewGuid();
    private static readonly Guid OrganizationId = Guid.NewGuid();

    // The outcomes of the ubiquitous language, in this order
    private static readonly string[] OutcomeNames = ["Succeeded", "Rejected", "Denied", "Failed"];

    // The complete list of what an audit record may hold (ADR-0016 point 3)
    private static readonly string[] AllowedProperties =
    [
        nameof(AuditRecord.Id), nameof(AuditRecord.OccurredAt), nameof(AuditRecord.ActorUserId), nameof(AuditRecord.Action),
        nameof(AuditRecord.TargetType), nameof(AuditRecord.TargetId), nameof(AuditRecord.OrganizationId),
        nameof(AuditRecord.Outcome), nameof(AuditRecord.ErrorCode)
    ];

    [Fact]
    public void Create_AllFields_KeepsEveryValueAndGivesANewId()
    {
        var record = AuditRecord.Create(
            UtcNow, ActorUserId, Action, TargetType, TargetId, OrganizationId, AuditOutcome.Rejected, ErrorCode);

        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.Equal(
            (UtcNow, (Guid?)ActorUserId, Action, (string?)TargetType, (Guid?)TargetId, (Guid?)OrganizationId,
                AuditOutcome.Rejected, (string?)ErrorCode),
            (record.OccurredAt, record.ActorUserId, record.Action, record.TargetType, record.TargetId,
                record.OrganizationId, record.Outcome, record.ErrorCode));
    }

    [Fact]
    public void Create_Twice_GivesDistinctIds()
    {
        var first = CreateSucceeded();
        var second = CreateSucceeded();

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Create_AnonymousWithoutTargetOrOrganization_IsAllowed()
    {
        var record = AuditRecord.Create(UtcNow, null, Action, null, null, null, AuditOutcome.Succeeded, null);

        Assert.Equal(((Guid?)null, (string?)null, (Guid?)null, (Guid?)null),
            (record.ActorUserId, record.TargetType, record.TargetId, record.OrganizationId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutAction_Throws(string? action)
    {
        Assert.Throws<ArgumentException>(() =>
            AuditRecord.Create(UtcNow, ActorUserId, action!, null, null, null, AuditOutcome.Succeeded, null));
    }

    [Fact]
    public void Create_TargetTypeWithoutTargetId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AuditRecord.Create(UtcNow, ActorUserId, Action, TargetType, null, null, AuditOutcome.Succeeded, null));
    }

    [Fact]
    public void Create_TargetIdWithoutTargetType_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AuditRecord.Create(UtcNow, ActorUserId, Action, null, TargetId, null, AuditOutcome.Succeeded, null));
    }

    [Fact]
    public void Create_SucceededWithErrorCode_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AuditRecord.Create(UtcNow, ActorUserId, Action, null, null, null, AuditOutcome.Succeeded, ErrorCode));
    }

    [Theory]
    [InlineData(AuditOutcome.Rejected)]
    [InlineData(AuditOutcome.Denied)]
    [InlineData(AuditOutcome.Failed)]
    public void Create_NonSucceededWithErrorCode_KeepsTheErrorCode(AuditOutcome outcome)
    {
        var record = AuditRecord.Create(UtcNow, ActorUserId, Action, null, null, null, outcome, ErrorCode);

        Assert.Equal((outcome, (string?)ErrorCode), (record.Outcome, record.ErrorCode));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Create_TimeNotInUtc_Throws(DateTimeKind kind)
    {
        var occurredAt = DateTime.SpecifyKind(UtcNow, kind);

        Assert.Throws<ArgumentException>(() =>
            AuditRecord.Create(occurredAt, ActorUserId, Action, null, null, null, AuditOutcome.Succeeded, null));
    }

    [Fact]
    public void AuditOutcome_HasExactlyTheFourOutcomes()
    {
        Assert.Equal(OutcomeNames, Enum.GetNames<AuditOutcome>());
    }

    [Fact]
    public void AuditRecord_HoldsNothingButTheAllowedFields()
    {
        var properties = typeof(AuditRecord).GetProperties().Select(p => p.Name).ToHashSet();

        Assert.Equal(AllowedProperties.ToHashSet(), properties);
    }

    [Fact]
    public void AuditRecord_HasNoPublicConstructorOrSetter()
    {
        Assert.Empty(typeof(AuditRecord).GetConstructors());
        Assert.All(typeof(AuditRecord).GetProperties(), p => Assert.False(p.SetMethod?.IsPublic ?? false, p.Name));
    }

    private static AuditRecord CreateSucceeded() =>
        AuditRecord.Create(UtcNow, ActorUserId, Action, null, null, null, AuditOutcome.Succeeded, null);
}

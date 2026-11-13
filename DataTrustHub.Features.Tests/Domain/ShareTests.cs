using System.Reflection;
using DataTrustHub.Domain.Shares;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.Domain;

// #3 (ADR-0015), the Share aggregate without HTTP or a database: a share gives one recipient read access to one
// data item; its expiry date, when given, is later than the moment of sharing (S4); it is active while it is not
// revoked and not expired (S5); revoking it a second time answers NotFound and keeps the first revocation time
public class ShareTests
{
    private const string ExpiryNotAfterSharedAtCode = "Share.ExpiryNotAfterSharedAt";

    private static readonly DateTime SharedAt = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(1);
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);
    private static readonly DateTime ExpiresAt = SharedAt + OneDay;
    private static readonly Guid DataItemId = Guid.NewGuid();
    private static readonly Guid RecipientUserId = Guid.NewGuid();
    private static readonly Guid SharedByUserId = Guid.NewGuid();

    public static TheoryData<string> ShareProperties => new()
    {
        nameof(Share.Id), nameof(Share.DataItemId), nameof(Share.RecipientUserId), nameof(Share.SharedByUserId),
        nameof(Share.SharedAt), nameof(Share.ExpiresAt), nameof(Share.RevokedAt)
    };

    // --- AC1: shape ---

    [Theory]
    [MemberData(nameof(ShareProperties))]
    public void Share_Property_HasAPrivateSetter(string propertyName)
    {
        var property = typeof(Share).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(property);
        Assert.True(property!.SetMethod?.IsPrivate, $"{propertyName} must have a private setter");
    }

    [Fact]
    public void Share_ExpiresAtAndRevokedAt_AreOptional()
    {
        Assert.Equal(
            (typeof(DateTime?), typeof(DateTime?), typeof(DateTime)),
            (typeof(Share).GetProperty(nameof(Share.ExpiresAt))!.PropertyType,
                typeof(Share).GetProperty(nameof(Share.RevokedAt))!.PropertyType,
                typeof(Share).GetProperty(nameof(Share.SharedAt))!.PropertyType));
    }

    // --- AC2: Create ---

    [Fact]
    public void Create_WithExpiry_StoresEveryField()
    {
        var share = Create(ExpiresAt);

        Assert.Equal(
            (DataItemId, RecipientUserId, SharedByUserId, SharedAt, (DateTime?)ExpiresAt, (DateTime?)null),
            (share.DataItemId, share.RecipientUserId, share.SharedByUserId, share.SharedAt, share.ExpiresAt, share.RevokedAt));
    }

    [Fact]
    public void Create_WithoutExpiry_Succeeds()
    {
        var result = Share.Create(DataItemId, RecipientUserId, SharedByUserId, SharedAt, expiresAt: null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ExpiresAt);
    }

    [Fact]
    public void Create_GivesEachShareItsOwnId()
    {
        var first = Create(null);
        var second = Create(null);

        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Create_ExpiryOneTickAfterSharedAt_Succeeds()
    {
        Assert.True(Share.Create(DataItemId, RecipientUserId, SharedByUserId, SharedAt, SharedAt + OneTick).IsSuccess);
    }

    [Fact]
    public void Create_ExpiryEqualToSharedAt_FailsWithAValidationShareError()
    {
        AssertExpiryRejected(Share.Create(DataItemId, RecipientUserId, SharedByUserId, SharedAt, SharedAt));
    }

    [Fact]
    public void Create_ExpiryBeforeSharedAt_FailsWithAValidationShareError()
    {
        AssertExpiryRejected(Share.Create(DataItemId, RecipientUserId, SharedByUserId, SharedAt, SharedAt - OneDay));
    }

    // --- AC3: IsActiveAt ---

    [Fact]
    public void IsActiveAt_NoExpiryNotRevoked_IsTrue()
    {
        Assert.True(Create(null).IsActiveAt(SharedAt + OneDay * 365));
    }

    [Fact]
    public void IsActiveAt_BeforeTheExpiry_IsTrue()
    {
        Assert.True(Create(ExpiresAt).IsActiveAt(ExpiresAt - OneTick));
    }

    [Fact]
    public void IsActiveAt_ExactlyAtTheExpiry_IsFalse()
    {
        Assert.False(Create(ExpiresAt).IsActiveAt(ExpiresAt));
    }

    [Fact]
    public void IsActiveAt_AfterTheExpiry_IsFalse()
    {
        Assert.False(Create(ExpiresAt).IsActiveAt(ExpiresAt + OneTick));
    }

    [Fact]
    public void IsActiveAt_RevokedWithoutExpiry_IsFalse()
    {
        var share = Create(null);
        share.Revoke(SharedAt + OneTick);

        Assert.False(share.IsActiveAt(SharedAt + OneDay));
    }

    [Fact]
    public void IsActiveAt_RevokedBeforeTheExpiry_IsFalse()
    {
        var share = Create(ExpiresAt);
        share.Revoke(SharedAt + OneTick);

        Assert.False(share.IsActiveAt(ExpiresAt - OneTick));
    }

    // --- AC4: Revoke ---

    [Fact]
    public void Revoke_ActiveShare_SucceedsAndSetsRevokedAt()
    {
        var share = Create(null);
        var revokedAt = SharedAt + OneDay;

        var result = share.Revoke(revokedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(revokedAt, share.RevokedAt);
    }

    [Fact]
    public void Revoke_AlreadyRevoked_FailsWithNotFound()
    {
        var share = Create(null);
        share.Revoke(SharedAt + OneDay);

        var result = share.Revoke(SharedAt + OneDay + OneDay);

        Assert.True(result.IsFailure);
        Assert.Equal(ShareErrors.NotFound, result.Error);
    }

    [Fact]
    public void Revoke_AlreadyRevoked_KeepsTheFirstRevocationTime()
    {
        var share = Create(null);
        var firstRevokedAt = SharedAt + OneDay;
        share.Revoke(firstRevokedAt);

        share.Revoke(firstRevokedAt + OneDay);

        Assert.Equal(firstRevokedAt, share.RevokedAt);
    }

    [Fact]
    public void Revoke_ExpiredButNotRevoked_Succeeds()
    {
        var share = Create(ExpiresAt);
        var revokedAt = ExpiresAt + OneDay;

        var result = share.Revoke(revokedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(revokedAt, share.RevokedAt);
    }

    [Fact]
    public void Revoke_KeepsTheOtherFields()
    {
        var share = Create(ExpiresAt);
        var id = share.Id;

        share.Revoke(SharedAt + OneTick);

        Assert.Equal(
            (id, DataItemId, RecipientUserId, SharedByUserId, SharedAt, (DateTime?)ExpiresAt),
            (share.Id, share.DataItemId, share.RecipientUserId, share.SharedByUserId, share.SharedAt, share.ExpiresAt));
    }

    // --- helpers ---

    private static Share Create(DateTime? expiresAt) =>
        Share.Create(DataItemId, RecipientUserId, SharedByUserId, SharedAt, expiresAt).Value;

    private static void AssertExpiryRejected(Result<Share> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal(ShareErrors.ExpiryNotAfterSharedAt, result.Error);
        Assert.Equal(ExpiryNotAfterSharedAtCode, result.Error.Code);
    }
}

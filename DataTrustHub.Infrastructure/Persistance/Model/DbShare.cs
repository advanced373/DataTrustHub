using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.Model
{
    // Shares of data items (ADR-0015). The indexes serve "Shared with me" (per recipient) and the S3 check
    // (per item and recipient). The foreign key to the data item lives in DbShareConfiguration; there are none
    // to the users, as for DbDataItem.OwnerUserId, so a share never blocks an account deletion
    [Index(nameof(RecipientUserId))]
    [Index(nameof(DataItemId), nameof(RecipientUserId))]
    public class DbShare: DbEntity
    {
        public required Guid DataItemId { get; set; }
        public required Guid RecipientUserId { get; set; }
        public required Guid SharedByUserId { get; set; }
        public required DateTime SharedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
    }
}

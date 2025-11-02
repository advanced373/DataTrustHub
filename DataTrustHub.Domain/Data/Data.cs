using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Data
{
    public sealed class DataItem : Entity, IAggregateRoot
    {
        public required Guid Id { get; set; }
        public required string Name { get; set; }
        public required long Size { get; set; }
        public string? Content { get; set; }
        public required Guid OwnerUserId { get; set; }
        public required string SecurityMarking { get; set; }
    }
}

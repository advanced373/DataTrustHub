using DataTrustHub.SharedKernel;
using ClassificationLevelValue = DataTrustHub.Domain.ClassificationLevel.ClassificationLevel;
namespace DataTrustHub.Domain.Policy
{
    public sealed class Policy : Entity, IAggregateRoot
    {
        public required Guid Id { get; set; }
        public required string Name { get; set; }
        public IReadOnlyCollection<ClassificationLevelValue> ClassificationLevels { get; set; } = [];
        public required Guid OrganizationId { get; set; }
    }
}

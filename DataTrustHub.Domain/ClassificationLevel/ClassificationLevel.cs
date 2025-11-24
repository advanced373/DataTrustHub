
namespace DataTrustHub.Domain.ClassificationLevel
{
    public sealed class ClassificationLevel
    {
        public required int Id { get; set; }
        public required string Name { get; set; }
        public int Priority { get; set; }
    }
}

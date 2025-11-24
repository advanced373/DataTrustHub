
namespace DataTrustHub.Infrastructure.Persistance.Model
{
    public class DbClassificationLevel
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public required int Priority { get; set; }
    }
}

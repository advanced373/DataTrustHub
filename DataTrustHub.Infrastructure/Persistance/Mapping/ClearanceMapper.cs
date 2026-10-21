using DataTrustHub.Domain.Clearances;
using DataTrustHub.Infrastructure.Persistance.Model;
using Riok.Mapperly.Abstractions;

namespace DataTrustHub.Infrastructure.Persistance.Mapping;

[Mapper]
internal static partial class ClearanceMapper
{
    [MapProperty(nameof(DbClearance.ClassificationLevel), nameof(Clearance.Level))]
    internal static partial Clearance ToDomain(this DbClearance source);

    // The repository swaps the level for a stored one with the same name and priority, if any
    [MapProperty(nameof(Clearance.Level), nameof(DbClearance.ClassificationLevel))]
    internal static partial DbClearance ToDb(this Clearance source);

    // Levels are identified by name and priority in the domain; the database key stays in Infrastructure
    [MapperIgnoreSource(nameof(DbClassificationLevel.Id))]
    private static partial ClassificationLevel ToDomain(DbClassificationLevel source);

    [MapperIgnoreTarget(nameof(DbClassificationLevel.Id))]
    private static partial DbClassificationLevel ToDb(ClassificationLevel source);
}

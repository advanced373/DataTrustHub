using DataTrustHub.Domain.Policies;
using DataTrustHub.Infrastructure.Persistance.Model;
using Riok.Mapperly.Abstractions;

namespace DataTrustHub.Infrastructure.Persistance.Mapping;

[Mapper]
internal static partial class PolicyMapper
{
    // DbPolicy.ClassificationLevels is an unused leftover of the initial schema
    [MapperIgnoreSource(nameof(DbPolicy.ClassificationLevels))]
    internal static partial Policy ToDomain(this DbPolicy source);

    [MapperIgnoreTarget(nameof(DbPolicy.ClassificationLevels))]
    internal static partial DbPolicy ToDb(this Policy source);

    // Copies onto a tracked row; the key never changes
    [MapperIgnoreSource(nameof(Policy.Id))]
    [MapperIgnoreTarget(nameof(DbPolicy.Id))]
    [MapperIgnoreTarget(nameof(DbPolicy.ClassificationLevels))]
    internal static partial void CopyTo(this Policy source, [MappingTarget] DbPolicy target);
}

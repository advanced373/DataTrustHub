using DataTrustHub.Domain.Shares;
using DataTrustHub.Infrastructure.Persistance.Model;
using Riok.Mapperly.Abstractions;

namespace DataTrustHub.Infrastructure.Persistance.Mapping;

// Entities are rebuilt through their internal constructors (visible to Infrastructure only); their
// setters stay private (ADR-0012)
[Mapper]
internal static partial class ShareMapper
{
    internal static partial Share ToDomain(this DbShare source);

    internal static partial DbShare ToDb(this Share source);

    // Copies onto a tracked row; the key never changes
    [MapperIgnoreSource(nameof(Share.Id))]
    [MapperIgnoreTarget(nameof(DbShare.Id))]
    internal static partial void CopyTo(this Share source, [MappingTarget] DbShare target);

    // datetime2 does not keep the kind; every stored time is UTC
    private static DateTime ToUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? ToUtc(DateTime? value) => value is DateTime time ? ToUtc(time) : null;
}

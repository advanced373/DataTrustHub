using DataTrustHub.Domain.Users;
using DataTrustHub.Infrastructure.Persistance.Model;
using Riok.Mapperly.Abstractions;

namespace DataTrustHub.Infrastructure.Persistance.Mapping;

// Entities are rebuilt through their internal constructors (visible to Infrastructure only); their
// setters stay private (ADR-0012)
[Mapper]
internal static partial class UserMapper
{
    internal static partial User ToDomain(this DbUser source);

    [MapperIgnoreSource(nameof(User.IsAdmin))]
    internal static partial DbUser ToDb(this User source);

    // Copies onto a tracked row; the key never changes
    [MapperIgnoreSource(nameof(User.IsAdmin))]
    [MapperIgnoreSource(nameof(User.Id))]
    [MapperIgnoreTarget(nameof(DbUser.Id))]
    internal static partial void CopyTo(this User source, [MappingTarget] DbUser target);
}

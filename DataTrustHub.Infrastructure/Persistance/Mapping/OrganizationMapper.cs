using DataTrustHub.Domain.Organizations;
using DataTrustHub.Infrastructure.Persistance.Model;
using Riok.Mapperly.Abstractions;

namespace DataTrustHub.Infrastructure.Persistance.Mapping;

[Mapper]
internal static partial class OrganizationMapper
{
    internal static partial Organization ToDomain(this DbOrganization source);

    internal static partial DbOrganization ToDb(this Organization source);

    // Copies onto a tracked row; the key never changes
    [MapperIgnoreSource(nameof(Organization.Id))]
    [MapperIgnoreTarget(nameof(DbOrganization.Id))]
    internal static partial void CopyTo(this Organization source, [MappingTarget] DbOrganization target);
}

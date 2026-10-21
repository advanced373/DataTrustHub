using DataTrustHub.Domain.Organizations;
using DataTrustHub.Infrastructure.Persistance.Model;
using Riok.Mapperly.Abstractions;

namespace DataTrustHub.Infrastructure.Persistance.Mapping;

[Mapper]
internal static partial class OrganizationMapper
{
    internal static partial Organization ToDomain(this DbOrganization source);

    internal static partial DbOrganization ToDb(this Organization source);
}

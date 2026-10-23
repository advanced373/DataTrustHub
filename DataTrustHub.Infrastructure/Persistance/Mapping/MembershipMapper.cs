using DataTrustHub.Domain.Memberships;
using DataTrustHub.Infrastructure.Persistance.Model;
using Riok.Mapperly.Abstractions;

namespace DataTrustHub.Infrastructure.Persistance.Mapping;

[Mapper]
internal static partial class MembershipMapper
{
    internal static partial Membership ToDomain(this DbMembership source);

    internal static partial DbMembership ToDb(this Membership source);

    // Copies onto a tracked row; the key never changes
    [MapperIgnoreSource(nameof(Membership.Id))]
    [MapperIgnoreTarget(nameof(DbMembership.Id))]
    internal static partial void CopyTo(this Membership source, [MappingTarget] DbMembership target);

    // Roles are stored by name; a name that is not a standard role means the stored data is corrupt
    private static MembershipRole ToRole(string name) =>
        MembershipRole.FindStandard(name) ?? throw new InvalidOperationException($"Unknown membership role '{name}'.");

    private static string ToName(MembershipRole role) => role.Name;
}

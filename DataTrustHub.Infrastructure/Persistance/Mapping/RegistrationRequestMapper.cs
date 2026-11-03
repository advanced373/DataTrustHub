using DataTrustHub.Domain.Registrations;
using DataTrustHub.Infrastructure.Persistance.Model;
using Riok.Mapperly.Abstractions;

namespace DataTrustHub.Infrastructure.Persistance.Mapping;

// Entities are rebuilt through their internal constructors (visible to Infrastructure only); their
// setters stay private (ADR-0012)
[Mapper]
internal static partial class RegistrationRequestMapper
{
    internal static partial RegistrationRequest ToDomain(this DbRegistrationRequest source);

    internal static partial DbRegistrationRequest ToDb(this RegistrationRequest source);

    // datetime2 does not keep the kind; every stored time is UTC
    private static DateTime ToUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

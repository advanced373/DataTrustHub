using DataTrustHub.Application.Abstractions.Messaging;
using OrganizationValue = DataTrustHub.Domain.Organization.Organization;

namespace DataTrustHub.Application.Organization.Get;

public sealed record GetOrganizationByIdQuery(Guid OrganizationId) : IQuery<OrganizationValue>;

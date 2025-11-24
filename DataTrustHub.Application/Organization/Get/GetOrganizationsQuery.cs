using DataTrustHub.Application.Abstractions.Messaging;
using OrganizationValue = DataTrustHub.Domain.Organization.Organization;

namespace DataTrustHub.Application.Organization.Get;

public sealed record GetOrganizationsQuery() : IQuery<List<OrganizationValue>>;

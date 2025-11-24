using DataTrustHub.Application.Abstractions.Messaging;
using PolicyValue = DataTrustHub.Domain.Policy.Policy;

namespace DataTrustHub.Application.Policy.Get;

public sealed record GetPolicyByIdQuery(Guid PolicyId) : IQuery<PolicyValue>;

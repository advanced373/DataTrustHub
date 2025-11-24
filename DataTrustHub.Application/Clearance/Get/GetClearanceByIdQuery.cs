using DataTrustHub.Application.Abstractions.Messaging;
using ClearanceValue = DataTrustHub.Domain.Clearance.Clearance;

namespace DataTrustHub.Application.Clearance.Get;

public sealed record GetClearanceByIdQuery(Guid ClearanceId) : IQuery<ClearanceValue>;

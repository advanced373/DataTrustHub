using DataTrustHub.Application.Abstractions.Messaging;
using UserValue = DataTrustHub.Domain.User.User;

namespace DataTrustHub.Application.User.Get;

public sealed record GetUserByIdQuery(Guid UserId) : IQuery<UserValue>;

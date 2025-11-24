using DataTrustHub.Application.Abstractions.Messaging;
using UserValue = DataTrustHub.Domain.User.User;

namespace DataTrustHub.Application.User.Get;

public sealed record GetUsersQuery() : IQuery<List<UserValue>>;

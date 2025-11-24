using DataTrustHub.Application.Abstractions.Messaging;
using DataItemValue = DataTrustHub.Domain.Data.DataItem;

namespace DataTrustHub.Application.Data.Get;

public sealed record GetDataItemsQuery() : IQuery<List<DataItemValue>>;

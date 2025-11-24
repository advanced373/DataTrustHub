using DataTrustHub.Application.Abstractions.Messaging;
using DataTrustHub.Domain.Data;
using DataTrustHub.SharedKernel;
using DataItemValue = DataTrustHub.Domain.Data.DataItem;

namespace DataTrustHub.Application.Data.Get;

public sealed class GetDataItemsQueryHandler : IQueryHandler<GetDataItemsQuery, List<DataItemValue>>
{
    private readonly IDataItemRepository _dataItemRepository;

    public GetDataItemsQueryHandler(IDataItemRepository dataItemRepository)
    {
        _dataItemRepository = dataItemRepository;
    }

    public async Task<Result<List<DataItemValue>>> Handle(GetDataItemsQuery request, CancellationToken cancellationToken)
    {
        var dataItems = await _dataItemRepository.GetAllAsync();
        return Result.Success(dataItems);
    }
}

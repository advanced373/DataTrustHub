using DataTrustHub.Application.Abstractions.Messaging;
using DataTrustHub.Domain.Data;
using DataTrustHub.SharedKernel;
using DataItemValue = DataTrustHub.Domain.Data.DataItem;

namespace DataTrustHub.Application.Data.Get;

public sealed class GetDataItemByIdQueryHandler : IQueryHandler<GetDataItemByIdQuery, DataItemValue>
{
    private readonly IDataItemRepository _dataItemRepository;

    public GetDataItemByIdQueryHandler(IDataItemRepository dataItemRepository)
    {
        _dataItemRepository = dataItemRepository;
    }

    public async Task<Result<DataItemValue>> Handle(GetDataItemByIdQuery request, CancellationToken cancellationToken)
    {
        var dataItem = await _dataItemRepository.GetByIdAsync(request.DataItemId);

        return dataItem is null
            ? Result.Failure<DataItemValue>(Error.NotFound("DataItem.NotFound", $"Data item with ID {request.DataItemId} was not found."))
            : Result.Success(dataItem);
    }
}

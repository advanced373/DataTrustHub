using DataTrustHub.Application.Abstractions.Messaging;
using DataTrustHub.Domain.Data;
using DataTrustHub.SharedKernel;
using MediatR;

namespace DataTrustHub.Application.Data.Delete
{
    public class DeleteDataItemCommandHandler : IRequestHandler<DeleteDataItemCommand, Result>
    {
        private readonly IDataItemRepository _dataItemRepository;

        public DeleteDataItemCommandHandler(IDataItemRepository dataItemRepository)
        {
            _dataItemRepository = dataItemRepository;
        }

        public async Task<Result> Handle(DeleteDataItemCommand request, CancellationToken cancellationToken)
        {
            var dataItem = await _dataItemRepository.GetByIdAsync(request.DataItemId);

            if (dataItem is null)
            {
                return Result.Failure(DataErrors.NotFound(request.DataItemId));
            }

            await _dataItemRepository.DeleteAsync(request.DataItemId);
            return Result.Success();
        }
    }
}


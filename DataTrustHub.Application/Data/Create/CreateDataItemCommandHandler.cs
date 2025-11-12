using DataTrustHub.Domain.Data;
using DataTrustHub.SharedKernel;
using MediatR;

namespace DataTrustHub.Application.Data.Create
{
    public class CreateDataItemCommandHandler : IRequestHandler<CreateDataItemCommand, Result<Guid>>
    {
        private readonly IDataItemRepository _dataItemRepository;

        public CreateDataItemCommandHandler(IDataItemRepository dataItemRepository)
        {
            _dataItemRepository = dataItemRepository;
        }

        public async Task<Result<Guid>> Handle(CreateDataItemCommand request, CancellationToken cancellationToken)
        {
            var dataItemId = Guid.NewGuid();
            var dataItem = new DataItem
            {
                Id = dataItemId,
                Name = request.Name,
                Size = request.Size,
                Content = request.Content,
                OwnerUserId = request.OwnerUserId,
                SecurityMarking = request.SecurityMarking
            };
            
            await _dataItemRepository.AddAsync(dataItem);
            return Result.Success(dataItemId);
        }
    }
}


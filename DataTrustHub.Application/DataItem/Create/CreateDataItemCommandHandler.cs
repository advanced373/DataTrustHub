using MediatR;

namespace DataTrustHub.Application.DataItem.Create
{
    public class CreateDataItemCommandHandler : IRequestHandler<CreateDataItemCommand, Guid>
    {
        public async Task<Guid> Handle(CreateDataItemCommand request, CancellationToken cancellationToken)
        {
            var dataItemId = Guid.NewGuid();
            // TODO: Persiste data item
            return await Task.FromResult(dataItemId);
        }
    }
}

using DataTrustHub.SharedKernel;
using MediatR;

namespace DataTrustHub.Application.Clearance.Create
{
    public class CreateClearanceForUserCommandHandler : IRequestHandler<CreateClearanceForUserCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(CreateClearanceForUserCommand request, CancellationToken cancellationToken)
        {
            var clearanceId = Guid.NewGuid();
            // TODO: Persiste clearance
            return await Task.FromResult(Result.Success(clearanceId));
        }
    }
}

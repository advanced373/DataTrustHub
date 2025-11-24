using DataTrustHub.Application.Abstractions.Messaging;
using DataTrustHub.Domain.Clearance;
using DataTrustHub.SharedKernel;
using MediatR;

namespace DataTrustHub.Application.Clearance.Delete
{
    public class DeleteClearanceCommandHandler : IRequestHandler<DeleteClearanceCommand, Result>
    {
        private readonly IClearanceRepository _clearanceRepository;

        public DeleteClearanceCommandHandler(IClearanceRepository clearanceRepository)
        {
            _clearanceRepository = clearanceRepository;
        }

        public async Task<Result> Handle(DeleteClearanceCommand request, CancellationToken cancellationToken)
        {
            var clearance = await _clearanceRepository.GetByIdAsync(request.ClearanceId);

            if (clearance is null)
            {
                return Result.Failure(ClearanceErrors.NotFound(request.ClearanceId));
            }

            await _clearanceRepository.DeleteAsync(request.ClearanceId);
            return Result.Success();
        }
    }
}


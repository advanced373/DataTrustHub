using ClearanceValue = DataTrustHub.Domain.Clearance.Clearance;
using DataTrustHub.Domain.Clearance;
using DataTrustHub.Domain.ClassificationLevel;
using DataTrustHub.SharedKernel;
using MediatR;

namespace DataTrustHub.Application.Clearance.Create
{
    public class CreateClearanceForUserCommandHandler : IRequestHandler<CreateClearanceForUserCommand, Result<Guid>>
    {
        private readonly IClearanceRepository _clearanceRepository;

        public CreateClearanceForUserCommandHandler(IClearanceRepository clearanceRepository)
        {
            _clearanceRepository = clearanceRepository;
        }

        public async Task<Result<Guid>> Handle(CreateClearanceForUserCommand request, CancellationToken cancellationToken)
        {
            var clearanceId = Guid.NewGuid();
            var clearance = new ClearanceValue
            {
                Id = clearanceId,
                Name = request.Name,
                UserId = request.UserId,
                PolicyId = request.PolicyId,
                ClassificationLevel = new ClassificationLevel
                {
                    Id = 0, // Will be set by database
                    Name = request.ClassificationLevelName,
                    Priority = request.ClassificationLevelPriority
                }
            };
            
            await _clearanceRepository.AddAsync(clearance);
            return Result.Success(clearanceId);
        }
    }
}

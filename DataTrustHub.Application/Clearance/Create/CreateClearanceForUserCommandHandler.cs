using MediatR;

namespace DataTrustHub.Application.Clearance.Create
{
    public class CreateClearanceForUserCommandHandler : IRequestHandler<CreateClearanceForUserCommand, Guid>
    {
        public async Task<Guid> Handle(CreateClearanceForUserCommand request, CancellationToken cancellationToken)
        {
            var clearanceId = Guid.NewGuid();
            // TODO: Persiste clearance
            return await Task.FromResult(clearanceId);
        }
    }
}

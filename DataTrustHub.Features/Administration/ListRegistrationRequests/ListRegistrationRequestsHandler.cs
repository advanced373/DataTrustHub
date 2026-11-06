using DataTrustHub.Domain.ReadModels.Registrations;
using DataTrustHub.Domain.Registrations;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features._Shared.Registration;
using DataTrustHub.SharedKernel;
using Microsoft.Extensions.Options;

namespace DataTrustHub.Features.Administration.ListRegistrationRequests;

public record ListRegistrationRequestsQuery;

// The platform admin lists the pending registration requests, oldest first (ADR-0026). Logs nothing, so no
// email reaches a log
public class ListRegistrationRequestsHandler(
    IRegistrationRequestRepository registrationRequests,
    IRegistrationRequestQueries registrationRequestQueries,
    IOptions<RegistrationOptions> options,
    TimeProvider timeProvider)
    : IQueryHandler<ListRegistrationRequestsQuery, Result<IReadOnlyList<RegistrationRequestSummary>>>
{
    private readonly IRegistrationRequestRepository _registrationRequests = registrationRequests;
    private readonly IRegistrationRequestQueries _registrationRequestQueries = registrationRequestQueries;
    private readonly RegistrationOptions _options = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider;

    // This query writes: R2's lazy purge first deletes the expired requests, through the repository, with a
    // set-based delete that runs at once. What is left are the pending requests
    public async Task<Result<IReadOnlyList<RegistrationRequestSummary>>> Handle(
        ListRegistrationRequestsQuery query,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await _registrationRequests.RemoveExpiredAsync(now - _options.PendingRequestLifetime, cancellationToken);

        // Oldest first, then by id; the read model carries no password hash
        var pending = await _registrationRequestQueries.ListPendingAsync(cancellationToken);

        var summaries = pending.Select(ToSummary).ToList();
        return Result.Success<IReadOnlyList<RegistrationRequestSummary>>(summaries);
    }

    // The database reads the time back without a kind; it was written in UTC. ExpiresAt is computed here, as
    // a date addition does not translate to SQL on every provider
    private RegistrationRequestSummary ToSummary(PendingRegistrationRequest request)
    {
        var requestedAtUtc = DateTime.SpecifyKind(request.RequestedAt, DateTimeKind.Utc);
        return new RegistrationRequestSummary(
            request.Id, request.Email, requestedAtUtc, requestedAtUtc + _options.PendingRequestLifetime);
    }
}

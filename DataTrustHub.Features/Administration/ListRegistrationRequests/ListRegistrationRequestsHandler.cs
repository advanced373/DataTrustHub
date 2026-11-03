using DataTrustHub.Domain.Registrations;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features._Shared.Registration;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DataTrustHub.Features.Administration.ListRegistrationRequests;

public record ListRegistrationRequestsQuery;

// The platform admin lists the pending registration requests, oldest first (ADR-0026). Logs nothing, so no
// email reaches a log
public class ListRegistrationRequestsHandler(
    DContext db,
    IRegistrationRequestRepository registrationRequests,
    IOptions<RegistrationOptions> options,
    TimeProvider timeProvider)
    : IQueryHandler<ListRegistrationRequestsQuery, Result<IReadOnlyList<RegistrationRequestSummary>>>
{
    private readonly DContext _db = db;
    private readonly IRegistrationRequestRepository _registrationRequests = registrationRequests;
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

        // Password hashes never leave the handler: only the summary fields are projected
        var pending = await _db.RegistrationRequests
            .AsNoTracking()
            // Oldest first; the id breaks ties so the order is stable
            .OrderBy(r => r.RequestedAt)
            .ThenBy(r => r.Id)
            .Select(r => new { r.Id, r.Email, r.RequestedAt })
            .ToListAsync(cancellationToken);

        var summaries = pending.Select(r => ToSummary(r.Id, r.Email, r.RequestedAt)).ToList();
        return Result.Success<IReadOnlyList<RegistrationRequestSummary>>(summaries);
    }

    // The database reads the time back without a kind; it was written in UTC. ExpiresAt is computed here, as
    // a date addition does not translate to SQL on every provider
    private RegistrationRequestSummary ToSummary(Guid id, string email, DateTime requestedAt)
    {
        var requestedAtUtc = DateTime.SpecifyKind(requestedAt, DateTimeKind.Utc);
        return new RegistrationRequestSummary(id, email, requestedAtUtc, requestedAtUtc + _options.PendingRequestLifetime);
    }
}

# ADR-0026: Sign-ups wait for approval as registration requests

- **Status:** Accepted
- **Date:** 2026-11-03
- **Related:** #172; [ADR-0025](0025-rate-limiting-and-audited-queries.md), [ADR-0024](0024-no-app-service-health-check-on-the-free-tier.md), [ADR-0023](0023-normalize-email-addresses.md), [ADR-0016](0016-audit-trail-for-commands-and-data-access.md), [ADR-0012](0012-clean-architecture-with-vertical-slices.md)

## Context

`POST /auth/register` is anonymous and created an account at once. The API is public, so anyone could
create accounts in bulk and fill the database. DataTrustHub holds access-controlled data; an account
should exist only when a platform admin has accepted the person.

## Decision

1. **Register creates a registration request, not an account.** A registration request holds the
   normalized email address (U7) and the password hash (`IPasswordHasher`), never the plain password,
   and the time it was made (UTC). Register answers **202 Accepted** with the request's id. Nobody can
   log in with it: login answers exactly as for an unknown account (U4). A platform admin approves a
   request (the user is created) or rejects it (the request is deleted); those routes come in the next
   PR of #172. Register stays the `RegisterUser` command, audited as before (ADR-0016).
2. **A separate table, not a pending status on users.** Registration requests live in their own table
   `RegistrationRequests`, as the aggregate `RegistrationRequest`. `Users` holds approved accounts only.
3. **One pending request per email address** (R1). Register refuses an email that already has an
   account (`User.EmailAlreadyInUse`, 409) or a pending request (`Registration.AlreadyPending`, 409).
   A unique index on the email enforces it under concurrency.
4. **Three limits against piling up,** in the configuration section `Registration`, each at least 1,
   checked when the host starts (a smaller value stops it):
   - `PermitsPerHourPerIp` (default 5): the rate limit policy `RateLimitPolicies.Registration`, a fixed
     window of one hour per client address, no queue; over it the answer is **429** (as in ADR-0025).
     Other endpoints are not affected.
   - `PendingRequestLifetimeDays` (default 14): a request older than this has expired (R2). It is
     deleted and its email can register again.
   - `MaxPendingRequests` (default 100): with this many pending requests, register answers **503**
     `Registration.QueueFull` and saves nothing (R3), until requests are approved, rejected or expire.
     A new `ErrorType.Unavailable` maps to 503.
5. **Expired requests are purged lazily,** at the start of every register (and, in the next PR, when the
   platform admin lists the requests), with one set-based delete that runs at once, outside the
   command's unit of work. There is no timer: on the Free tier the app sleeps when idle (ADR-0024), so a
   timer would not run.
6. **The client address is the connection's remote address.** App Service's front end passes the
   client's own address to the app, so no forwarded headers are read. This is an assumption about the
   host: behind another proxy all clients would share the proxy's address, and the limit would become
   global. A request without an address shares one partition. The limit is per address, so it is weak
   against an IPv6 client that rotates addresses inside its own prefix; the cap still bounds the queue.
7. The time comes from a `TimeProvider` (the system clock in the host), so tests can control it.

## Alternatives considered

- **A pending status on `Users`.** Rejected: every query and rule that reads users (login, find by email,
  memberships, uniqueness) would have to exclude pending accounts, and forgetting one would let an
  unapproved person in. A separate table keeps `Users` meaning "approved accounts".
- **A timer or background job for the purge.** Rejected: the Free tier app sleeps, so it would not run
  reliably, and a lazy purge is enough while the queue is capped.
- **Read `X-Forwarded-For`.** Rejected for now: App Service already gives the client address, and
  trusting a header the client can set would let one client spread over many partitions.
- **Rate limit per email.** Rejected: one client could still submit many requests with different
  addresses; the cap and the per-address limit together bound the queue.

## Consequences

**Easier:**
- `Users` holds approved accounts only; nothing that reads users changes.
- The queue is bounded: at most `MaxPendingRequests` pending requests, and each client address adds at
  most `PermitsPerHourPerIp` an hour. The cap is soft: the count and the save are not atomic, so
  concurrent registers can pass it by a few requests.

**Harder:**
- Nobody can sign up and use the platform until a platform admin acts; until the next PR adds the
  approve route, register only fills the queue.
- One client can fill the queue in `MaxPendingRequests / PermitsPerHourPerIp` hours from one address, or
  faster from several; then everyone gets 503 until the requests are handled or expire.
- The limiter counts in memory per instance and resets on restart (as in ADR-0025).
- The purge is not part of the register's unit of work: if the register then fails, the expired
  requests stay deleted, which is harmless.

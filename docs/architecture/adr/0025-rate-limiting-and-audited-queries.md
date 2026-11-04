# ADR-0025: Rate limit and audit lookups that can confirm an account exists

- **Status:** Accepted
- **Date:** 2026-11-03
- **Related:** #122; [ADR-0016](0016-audit-trail-for-commands-and-data-access.md), [ADR-0012](0012-clean-architecture-with-vertical-slices.md), [ADR-0014](0014-organizations-as-tenants-with-memberships.md)

## Context

An organization admin finds a user by their exact email address to add them as a member or guest
(`POST /organizations/{organizationId}/users/find-by-email`, ADR-0014). The answer tells whether a
platform account exists for that address. An organization admin, or a stolen admin token, could
call it many times to test a list of addresses, and nothing recorded those calls: ADR-0016 audits
commands and data access only, and this lookup is a query.

## Decision

1. **Per-caller rate limit on lookups that can confirm an account exists.** ASP.NET Core's built-in
   rate limiter, with a named policy `RateLimitPolicies.UserLookup`: a fixed window of 10 requests
   per minute, no queue, partitioned by the caller's user id. A request without a caller id falls
   into one shared partition (the endpoint requires authentication anyway). The limit is per
   caller, not per organization: an admin of several organizations has one budget. A rejected
   request answers **429 Too Many Requests**, without detail. Endpoints opt in with
   `RequireRateLimiting`; the middleware runs after authorization, so the caller is known.
2. **Queries opt in to auditing.** A query that implements the marker `IAuditedQuery` is recorded by
   an `AuditQueryDecorator`, the outermost decorator of every query handler (outside validation, so
   rejected input is recorded too). It behaves like the command decorator of ADR-0016: one record per
   call, outcome `Succeeded`, `Rejected` with the error code, or `Failed` with the exception type;
   a failed audit write is logged and never changes the answer. The action is the query type name
   without `Query` (`FindUserByEmail`). `IOrganizationScopedQuery` declares the organization; what
   the handler puts in `AuditScope` wins, as for commands. A query without the marker is not audited.
3. **Find-by-email is audited.** The record names the actor, the organization and, when the user is
   found, the target user. It never holds the email address searched for.
4. A request refused by the rate limiter never reaches the handler and leaves no audit record.

## Alternatives considered

- **Audit every query.** Rejected: lists and reads would flood the trail, the reason ADR-0016 kept
  queries out.
- **Limit per organization.** Rejected: an admin of several organizations would get one budget per
  organization.
- **Record the searched email.** Rejected: an audit record never holds an email address, and the
  audit table has no column for it.
- **A rate limiting package.** Rejected: the built-in limiter of the shared framework is enough.

## Consequences

**Easier:**
- Another lookup that can confirm an account exists opts in with one `RequireRateLimiting` call and
  `IAuditedQuery`.
- The command and query audit decorators share one implementation of the record and its failure
  handling.

**Harder:**
- The limiter keeps its counters in memory per instance: with several App Service instances each one
  counts on its own, and a restart resets them. Acceptable while the API runs as one instance.
- Each audited lookup costs one insert in the audit table.

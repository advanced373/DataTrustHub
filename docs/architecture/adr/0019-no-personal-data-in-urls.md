# ADR-0019: Personal data stays out of URLs

- **Status:** Accepted
- **Date:** 2026-10-24
- **Related:** #95 (step 5a), #122; [ADR-0016](0016-audit-trail-for-commands-and-data-access.md),
  [ADR-0017](0017-azure-deployment.md)

## Context

#95 step 5a added a lookup that lets an organization admin find a user by email. The first
version took the email in the query string of a GET. In Azure (ADR-0017), the full URL of every
request, query string included, is stored by App Service HTTP logs and by Application Insights
request telemetry. Those are technical logs, outside the audit trail (ADR-0016), with their own
retention and access. An email address in a URL therefore leaks personal data into them.

## Decision

- **Personal data never goes in a URL:** not in the path, not in the query string. Email
  addresses, names and similar values travel in the request body.
- A lookup by such a value is a `POST` with a JSON body, even though it changes nothing. It stays a
  query in the code (`IQueryHandler`). Its route names the lookup, for example
  `POST /organizations/{organizationId}/users/find-by-email`, so it does not read as "create".
- Identifiers (GUIDs) may stay in URLs.

## Alternatives considered

- **Keep the GET and filter the logs.** Rejected: every log sink would need its own filter, and a
  new one would leak again by default.
- **Hash the email in the URL.** Rejected: a hash of an email is easy to reverse with a list of
  addresses, and the API would need a second form of the same value.

## Consequences

- The logs stay free of email addresses.
- Lookups by personal data are not cacheable as GETs; none needs to be.
- Reviewers check new routes for personal data in the URL.

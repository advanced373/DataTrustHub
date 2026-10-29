# ADR-0023: Email addresses are trimmed and lower-cased as a whole

- **Status:** Accepted
- **Date:** 2026-10-29
- **Related:** #123

## Context

Registration, admin account creation, login and find-user-by-email compare the email address
exactly as typed. Surrounding spaces are kept, and case depends on the database collation. So
`" Ion@Firma.RO "` can fail to sign in to the account registered as `ion@firma.ro`. The same person can
also end up with two accounts, which breaks rule U1 ("an email belongs to at most one user") in
practice.

RFC 5321 lets the part before `@` be case-sensitive, but mail providers in practice treat it as
case-insensitive. The domain part is always case-insensitive.

## Decision

- An email address is normalized by trimming surrounding whitespace and lower-casing the **whole**
  address with the invariant culture: `" Ion@Firma.RO "` becomes `ion@firma.ro`.
- The rule lives in one place, a value object in the Domain (`Domain/Users`). It is the form that is
  stored, compared, put in the JWT and returned.
- Existing stored addresses are normalized by a data migration. The migration stops, changing
  nothing, if normalizing would give two users the same address.

## Alternatives considered

- **Trim, and lower-case only the domain part.** Follows the RFC to the letter, but `Ion@…` and
  `ion@…` would stay two accounts, which is the problem this decision fixes.
- **A case-insensitive collation on the column, no normalization.** Fixes case but not spaces, and
  it depends on the database. The in-memory and SQLite test providers would behave differently.
- **Normalize in each use case.** The rule would be copied five times and drift apart.

## Consequences

- An address is matched whatever its case or surrounding spaces. Users see their address in lower case.
- A mailbox whose server really does treat case in the local part as significant cannot have two
  accounts that differ only in case. This is an accepted limit.
- The unique index for U1 (#121) can rely on the normalized form.

# ADR-0015: Sharing data items without breaking need-to-know

- **Status:** Accepted
- **Date:** 2026-10-22
- **Related:** #3; [ADR-0011](0011-data-access-by-policy-and-classification-level.md),
  [ADR-0014](0014-organizations-as-tenants-with-memberships.md); `docs/domain/`

## Context

Sharing (#3) is part of the MVP, but the issue only said "add metadata: security marking,
permissions, policies". Access today comes from clearances (ADR-0011), and ADR-0014 gives external
users a way in as guests. Sharing must fit that model. It must not become a way around
need-to-know. The user decided the rules on 2026-10-22.

## Decision

1. **A share gives one user read access to one data item.** It records the data item, the
   recipient, who shared it, when, and an optional expiry date.
2. **Only the owner shares.**
   - A personal item can be shared by its owner.
   - A classified item can be shared by its owner only while they are a member of the item's
     organization.
3. **Personal items can be shared with any user of the platform.** The share is what gives the
   recipient access.
4. **Classified items can be shared only with a user whose clearance already allows reading
   them**, i.e. a clearance on the item's policy at or above its level.
   - A share like this grants nothing new. It brings the item to the recipient's attention and
     lists it under *Shared with me*.
   - If the recipient later loses that clearance, the share no longer gives access. A share
     never overrides a clearance.
5. **A share is read-only:** view and download. A recipient cannot delete, re-share or change the
   item.
6. **Expiry and revocation:**
   - A share can have an expiry date. After it, the share is ignored.
   - The owner can revoke a share at any time.
   - Deleting the item ends all its shares.
7. **The read rule becomes:**
   - **personal item:** readable by its owner, and by holders of an active share;
   - **classified item:** readable only through a clearance (ADR-0014). A share only makes it
     visible in a list;
   - **deleted item:** readable by no one.

   Unreadable items still answer 404.

### Model (DDD)

**`Share` is its own aggregate**, in the Data Management context, with its own repository:
`ShareId`, `DataItemId`, `RecipientUserId`, `SharedByUserId`, `SharedAt`, `ExpiresAt?`, `RevokedAt?`.
- **Why not inside the `DataItem` aggregate:** a share is created and revoked on its own, and the
  "shared with me" list is read per recipient.

**Invariants:**

| # | Invariant | Enforced in |
|---|---|---|
| S1 | Only the owner shares or revokes. For a classified item, only while a member of its organization | Use case: share / revoke (entity check on `DataItem`) |
| S2 | A classified item is shared only with a recipient whose clearance allows reading it | Use case: share (`Clearance.Allows`) |
| S3 | At most one active share per item and recipient | Use case: share |
| S4 | An expiry date, if given, is in the future | Entity: `Share` |
| S5 | A share is active if it is not revoked, not expired, and its item is not deleted | Entity: `Share.IsActiveAt(now)` |
| D3″ | Personal item: readable by its owner or an active share. Classified item: readable through a clearance only. Deleted item: readable by no one | Entity: `DataItem.IsReadableBy`, plus the SQL form |

## Alternatives considered

- **Only personal items can be shared.** Rejected: the user wants classified items to be shareable
  within the clearance rules.
- **Classified shares as an exception, approved by an organization admin.** Rejected: it would be
  a deliberate bypass of need-to-know.
- **Anyone who can read may re-share.** Rejected: only the owner decides who sees their data.
- **Read and delete rights for recipients.** Rejected: a share is read-only.
- **Permanent shares only.** Rejected: an expiry date limits access that people forget to revoke.

## Consequences

**Easier:**
- Users can show a document to a specific person without changing anyone's clearances.

**Harder:**
- The read rule gains a third input (shares), in both the entity and the SQL form. The equivalence
  test between the two forms (ADR-0012 step 4) becomes essential, and must cover expiry and
  revocation.
- Account deletion and data item deletion must end the related shares.
- Every share, revocation and shared access is audited (ADR-0016).

**Follow-up (#3):**
- the domain (`Share`, the new read rule) and the migration;
- the use cases: share, revoke, *Shared with me*;
- the WebApp: a share dialog on a data item, and a *Shared with me* list.

It depends on ADR-0014 being implemented, because of organization membership.

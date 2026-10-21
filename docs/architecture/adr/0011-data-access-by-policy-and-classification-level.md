# ADR-0011: Data access by policy and classification level, need-to-know

- **Status:** Accepted
- **Date:** 2026-10-21
- **Related:** #5, #67, #68, #69, #70

## Context

DataTrustHub stores documents whose access is governed by policies and clearances. A clearance
gives a user a classification level (a name and a priority) on one policy, and a policy belongs to
an organization (group). Until #5, data items were linked to no policy, always carried the marking
`UNCLASSIFIED`, and only their owner could read them. There was nothing to decide access against.

Users are also not linked to organizations in the data model. Linking them would need its own
migration and admin UI.

## Decision

1. **A data item is personal or classified.**
   - A personal item has no policy, and only its owner reads it.
   - A classified item belongs to a policy (`DataItems.PolicyId`) and has a level
     (`DataItems.ClassificationPriority`, with `SecurityMarking` holding the level's name).
2. **The read rule.** A non-deleted item is visible to:
   - its owner;
   - any user with a clearance **on the item's policy** whose priority is **≥ the item's level**.

   The rule is defined once, in `Features/DataManagement/DataAccess.VisibleTo`, and is used by
   `GET /data` and `GET /data/{id}`. An item the user may not read answers `404`.
3. **Need-to-know.** The `Admin` role grants no read access. Admins manage policies and clearances,
   and see data only through their own clearances.
4. **Classification at upload.** The uploader picks a policy they hold a clearance on, and a level at
   most their own clearance on that policy, or leaves the item personal. The API enforces this (`400`
   otherwise). `GET /data/classification-options` tells the UI which choices are allowed.
5. **Fixed levels.** The levels are Public 0, Internal 1, Confidential 2, Secret 3 and Top Secret 4
   (`DataManagement/ClassificationLevels`). Access is compared on the priority.
6. **Only the owner deletes** an item ([ADR-0004](0004-owner-only-soft-delete.md), unchanged).

## Alternatives considered

- **Organization membership plus level.** Users belong to a group, and see the group's data at their
  level on any of its policies. It needs a user–organization link (a migration and admin UI), and it
  is coarser than per-policy access.
- **Admins see everything.** Simpler for support, but it breaks need-to-know, the core promise of a
  classified-data platform.
- **Free classification at upload.** An uploader could mark a file above their own clearance and then
  only read it as its owner. Restricting it keeps classifications consistent with clearances.
- **Only admins classify.** Safer, but every upload would wait for an admin, and it needs a
  classification queue in the UI.

## Consequences

- Access control is enforced in SQL by a single query rule, so lists stay efficient and no item
  outside a user's clearances is loaded.
- Existing items stay personal; no data was reclassified.
- `ClassificationLevels` lists the levels for data items, while clearances still accept any
  name/priority through the admin API. The comparison uses priorities only, so the names may differ.
- The rule lives in the DataManagement feature. Sharing (#3) will need the same rule, and Rule 2 forbids
  importing another feature, so the rule must move to a shared place first. Where it should live is
  part of the open Domain question (see #45 and the architecture discussion of 2026-10-21).

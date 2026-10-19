# ADR-0004: Owner-only soft delete for data items

- **Status:** Accepted
- **Date:** 2026-06-16
- **Related:** [Delete design spec](../../superpowers/specs/2026-06-16-delete-data-item-design.md), #7, #20

## Context

The legacy delete (`Application/Data/Delete`) hard-deleted any data item by id, without checking
who asked. In a platform built around access control, that is a security hole, and a hard delete
removes anything an audit could later need.

## Decision

`DELETE /data/{id}` (`DataManagement/DeleteDataItem`):
- Only the owner can delete. The ownership check is part of the EF query
  (`Id == id && OwnerUserId == requester && !IsDeleted`), so other users' rows are never loaded.
- Soft delete: sets `DbDataItem.IsDeleted = true`; the row stays.
- A non-owner, a missing item and an already deleted item all get `404`, so the API does not
  reveal that someone else's item exists. Success returns `204`.

## Alternatives considered

- **Hard delete.** Loses data needed for recovery and audit.
- **`403` for non-owners.** Leaks the existence of other users' items, and needs a new
  `ErrorType` in `SharedKernel`.

## Consequences

- Every query that reads data items must filter `IsDeleted`. A global EF query filter would
  enforce this centrally; it is not in place yet, and the legacy `GET /DataItem` does not filter (#41).
- Deleted rows still take space. Purging is a future decision.

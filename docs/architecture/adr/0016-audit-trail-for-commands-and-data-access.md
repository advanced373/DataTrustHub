# ADR-0016: Audit trail for commands and data access

- **Status:** Accepted
- **Date:** 2026-10-22
- **Related:** #24, #6, #22; [ADR-0012](0012-clean-architecture-with-vertical-slices.md)

## Context

DataTrustHub decides who may see sensitive data. It has to be able to show who did what and who
read what. The old audit pipeline (#6, PR #15) was a MediatR behavior in the removed Application
layer. It exists only on `main`. ADR-0012 removed MediatR and planned an audit decorator. The
user decided on 2026-10-22 that the MVP audits commands and data access, and that admins can see
the trail.

## Decision

1. **Every command is audited** by an `AuditCommandDecorator`. Scrutor wraps it around every
   `ICommandHandler`, outside the validation decorator, so rejected input is recorded too.
2. **Data access is audited** for:
   - every download of a data item;
   - every attempt to open an item that answers 404 because the user may not read it.

   It is written by the read use cases themselves, through an `IAuditLog` port, because a query
   decorator cannot tell "denied" from "does not exist". Plain list queries are not audited.
3. **An audit record holds:**
   - when it happened (UTC);
   - who did it (user id, or anonymous);
   - the action (the command or access name, e.g. `DeleteDataItem`, `DataItem.Download`);
   - the target (type and id, when there is one);
   - the organization (once ADR-0014 is built);
   - the outcome: `Succeeded`, `Rejected` (validation or a domain error, with its error code),
     `Denied` (not readable) or `Failed` (an exception).

   It **never** holds file content, passwords, hashes or tokens.
4. **The trail is append-only.** No use case updates or deletes audit records.
5. **The trail survives a failed command.** The record is written in its own save, after the
   command's unit of work, so a rolled-back command still leaves a `Rejected` or `Failed` record.
6. **Who sees it:**
   - **platform admins** see tenant-level actions;
   - **organization admins** see their organization's trail (ADR-0014).

   The trail is shown on an admin page, filtered by user, action and date range.
7. **The old audit on `main` is not ported.** It is replaced by this design. When `main` is
   reconciled (#22), its two audit commits are dropped.

## Alternatives considered

- **Audit commands only.** Rejected: for a trust platform, who read what matters as much as who
  changed what.
- **A query decorator for data access.** Rejected: the decorator only sees "not found". It cannot
  tell a denied read from a missing item, and it would also log every list.
- **Write the audit inside the command's transaction.** Rejected: a rolled-back command would lose
  its audit record. Failed and rejected attempts are the ones that matter most.
- **Log files or Application Insights only.** Rejected for the trail: it must be queryable per
  organization and shown to admins. Technical logs still go to Application Insights (ADR-0017).

## Consequences

**Easier:**
- Every new command is audited automatically, through the decorator.
- Admins get a trail without any change to the slices.

**Harder:**
- The audit table grows without limit. Retention and archiving come after the MVP.
- The read use cases must call `IAuditLog` explicitly. The reviewer checks this.
- It adds a migration (`AuditRecords`), done by the Database Owner.

**Follow-up (#24):**
1. the audit record, the `IAuditLog` port and its adapter, and the migration;
2. the command decorator;
3. data-access auditing in the download and get use cases;
4. the admin page.

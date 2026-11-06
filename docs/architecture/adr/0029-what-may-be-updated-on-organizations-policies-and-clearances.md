# ADR-0029: What may be updated on organizations, policies and clearances

- **Status:** Accepted
- **Date:** 2026-11-06
- **Related:** #23; [ADR-0011](0011-data-access-by-policy-and-classification-level.md), [ADR-0014](0014-organizations-as-tenants-with-memberships.md)

## Context

Organizations, policies and clearances can be created, listed, read and deleted, but not updated.
A typo in a name, or a clearance granted at the wrong level, could only be fixed by deleting and
creating again. For a policy that already has clearances, that is not possible at all (P3).

Updating raises three questions: who may update each one, what may change, and whether the
four-eyes rule for granting clearances (C8) also covers changing them.

## Decision

1. **Organizations are renamed by the platform admin only**
   (`PUT /admin/organizations/{organizationId}`). This matches create and delete. Names stay unique
   (O1).
2. **Policies are renamed by an organization admin of their organization**
   (`PUT /organizations/{organizationId}/policies/{policyId}`). Names stay unique within the
   organization (P2). The platform admin does not manage policies (ADR-0014). A policy never moves to
   another organization.
3. **A clearance's name and classification level may change; its user and policy may not**
   (`PUT /organizations/{organizationId}/clearances/{clearanceId}`). To give a clearance to another
   user or on another policy, revoke it and grant a new one. The new level applies to data access
   right away.
4. **An organization admin cannot change their own clearance** (`Clearance.CannotChangeOwn`, 409).
   This extends C8: raising one's own level is the same as granting oneself access. Another
   organization admin of the organization must change it.
5. Every update answers `204 No Content`. Sending the current values is not a conflict. Updates are
   audited like every other command (ADR-0016).

## Alternatives considered

- **Let an organization admin rename their own organization**: not chosen. The organization's name is
  visible to all its members and in the platform admin's views, and creating and deleting
  organizations is also the platform admin's job.
- **Change only the level of a clearance, not its name**: not chosen. The name describes why the
  clearance was granted, and it should be correctable without revoking the clearance.
- **Let a clearance's user or policy change**: not chosen. Then a clearance could be moved to another
  user without C5 and C8 being checked again. Revoking and granting a new clearance runs every check.
- **Allow changing one's own clearance when the level goes down**: not chosen. It makes the rule
  harder to explain, and revoking one's own clearance (allowed by C8) already removes access.

## Consequences

- Three new command slices: `RenameOrganization`, `RenamePolicy`, `UpdateClearance`, each with an
  entity method and a repository `UpdateAsync`. No schema change.
- A new domain error, `Clearance.CannotChangeOwn`, documented next to C8 in the domain handbook.
- The WebApp has no edit forms yet; they are a follow-up.

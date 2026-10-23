# ADR-0014: Organizations as tenants, with memberships, organization admins and guests

- **Status:** Accepted
- **Date:** 2026-10-22; amended 2026-10-22 (C8) and 2026-10-23 (M3)
- **Related:** #95 (implementation); [ADR-0004](0004-owner-only-soft-delete.md),
  [ADR-0006](0006-roles-in-jwt-and-admin-seeding.md),
  [ADR-0011](0011-data-access-by-policy-and-classification-level.md); `docs/domain/`

## Context

The domain handbook brought a gap to light. The project description says "users belong to
organizations", but the model has no such link:

- `User` has no organization.
- An organization only groups policies.
- A user is connected to organizations only through clearances. Clearances can be on policies of
  any organization, and a single global `Admin` role manages everything.

Without a link between users and organizations, these questions have no answer:
- What does it mean that someone works for an organization?
- What happens when they leave?
- Who may manage an organization?

The user decided the business model in a brainstorming session on 2026-10-22. This ADR records
it.

## Decision

### Business model

1. **An organization is a tenant:** a company on the platform. Its policies, clearances and
   classified data belong to it.
2. **A user is a platform identity.** A user can be a member of **several** organizations at the
   same time, through a **membership**.
3. **A membership has a role in that organization:**
   - **Member:** holds clearances and reads and uploads data, as today.
   - **Guest:** an external person, such as a consultant from another organization. A guest is
     added explicitly and then works like a member: with a clearance, a guest reads and uploads
     under the host's policies. A guest cannot become an organization admin. The guest role is
     fixed (M3, decided on 2026-10-23): to make a guest an internal member, the membership is ended
     and the person is added again.
   - **OrgAdmin:** manages the organization: its members, policies and clearances.
4. **Access to another organization's data is possible, but only explicitly.** The user is first
   added to that organization as a guest, and then given a clearance there. A clearance never
   crosses the tenant boundary on its own.
5. **The platform admin manages tenants only.** This is today's `Admin` role.
   - It creates and deletes organizations and appoints their first organization admin.
   - It does not manage policies, members or clearances inside an organization, and has no
     access to data.
6. **Need-to-know is kept (ADR-0011).** No admin role grants read access: platform admins,
   organization admins and guests all need a clearance.
7. **Classified data belongs to the organization:**
   - **Read:** only by holders of a clearance on the item's policy, at or above its level. The
     owner has **no** special right on classified data. They could upload it only with a
     clearance, so they read it for as long as they hold one. This changes ADR-0011.
   - **Delete:** by the owner while they are a member of the organization, or by an organization
     admin of the item's organization. The admin can delete without being able to read the
     content. This changes ADR-0004.
   - **When the owner leaves:** the items stay with the organization.
8. **Personal data stays at user level.** A data item with no policy belongs to its owner, outside
   any organization. Only the owner reads and deletes it, as today.
9. **Leaving an organization** ends the membership and revokes the user's clearances on that
   organization's policies, in the same transaction. Clearances are **never granted
   automatically**: every grant is a deliberate act of an organization admin. That admin is
   never the grantee: an organization admin cannot grant a clearance to themselves (C8).
10. **Deleting an account:**
    - it ends all the user's memberships, and so revokes their clearances;
    - it soft-deletes the user's personal data;
    - classified data stays with its organizations. This changes rule U5 of the domain handbook.

### Invariants

| # | Invariant | Enforced in |
|---|---|---|
| M1 | A user has at most one membership per organization | Use case: add member (`ExistsAsync`) |
| M2 | An organization always has at least one organization admin, so the last one cannot be removed or demoted | Use case: remove member / change role |
| M3 | The guest role is fixed: a guest cannot change to another role, and no member or organization admin becomes a guest | Entity: `Membership.ChangeRole` |
| C5 | A clearance on a policy of organization X can be granted only to a member or guest of X | Use case: create clearance |
| C6 | Only an organization admin of X grants or revokes clearances on X's policies, and creates or deletes X's policies | Use case + authorization |
| C7 | Ending a membership in X revokes every clearance of that user on X's policies | Use case: leave / remove member / delete account |
| C8 | An organization admin cannot grant a clearance to themselves (four-eyes) | Use case: create clearance |
| D3′ | A classified item is readable only through a clearance. A personal item is readable only by its owner. A deleted item is readable by no one | Entity: `DataItem.IsReadableBy`, plus the SQL form |
| D5′ | A classified item is deleted by its owner while a member of its organization, or by an admin of its organization. A personal item is deleted by its owner only | Entity: `DataItem.MarkDeleted` |

### Model (DDD)

- **Membership is its own aggregate** (`MembershipId`, `UserId`, `OrganizationId`, `Role`), in the
  Access Governance context, with its own repository.
  - **Why not inside the `Organization` aggregate:** loading every member to change one would make
    each command heavy, and it would conflict under concurrent edits.
  - **Cost:** M1 and M2 are checked in the use case, like the existing O2 and P2.
- **`MembershipRole`** (`Member`, `Guest`, `OrgAdmin`) is a value object.
- **The platform role stays on `User`:** `UserRoles.Admin` means platform admin. Roles inside an
  organization live on the membership.
- **Authorization inside an organization** is decided per request from the memberships, not from
  the JWT. A user can belong to many organizations, and their membership can change while a token
  is still valid.
- **C7 runs in the use case today,** in one transaction, like the account-deletion cascade. If the
  contexts become separate services, it becomes a `MembershipEnded` domain event handled by Access
  Governance.

### Migration of existing data

- Every user holding a clearance on a policy of organization X becomes a `Member` of X.
- Every current admin stays a platform admin. Each also becomes an `OrgAdmin` of every existing
  organization, so that no organization starts without an admin (M2).
- Existing classified items stay as they are. Under D3′, their owners keep access only through a
  clearance.

## Alternatives considered

- **Keep the current model,** where an organization is only a container for policies. Rejected:
  the organization is a tenant in reality, and the model would leave "works for", "leaves" and
  "manages" undefined.
- **Exactly one organization per user.** Rejected: users can work for several organizations, and
  guests need a second one.
- **Direct clearances for external users, without a membership.** Rejected: external access would
  be invisible in the organization's member list, and the tenant boundary would blur.
- **Membership inside the `Organization` aggregate.** Rejected: see *Model* above. It would make M2
  an entity invariant, but at the cost of heavy loads and write conflicts.
- **Owner keeps reading their classified items.** Rejected: it contradicts "the data belongs to the
  organization". It would also let a former member keep access.
- **A platform admin who manages everything.** Rejected: tenants manage themselves, and the
  platform role stays small.

## Consequences

**Easier:**
- Tenant boundaries are explicit.
- External access is visible.
- Leaving an organization removes access in a single step.
- Organizations administer themselves.

**Harder:**
- Authorization checks memberships on every organization-scoped request.
- The read rule changes in both forms (entity and SQL).

**Changed decisions:**
- ADR-0011 read rule: the owner no longer has a special right on classified items.
- ADR-0004: organization admins may also delete classified items.
- ADR-0006: `Admin` now means platform admin, and organization roles live in memberships.
- The domain handbook updates U5, D3 and D5, and adds the `Membership` aggregate, when this is
  implemented.

**Follow-up (#95), in small stacked PRs:**
1. domain (`Membership`, `MembershipRole`, the new rules on `DataItem`) and the migration, with the
   Database Owner role;
2. membership use cases (add member or guest, change role, remove, leave);
3. organization-scoped authorization for policies and clearances, and the platform admin limits;
4. the read and delete rules, with the entity/SQL equivalence test;
5. WebApp pages for members and organization admins.

**Decided on 2026-10-22: no self-grant (C8).** An organization admin cannot grant a clearance to
themselves; another organization admin of the same organization must grant it. Otherwise one admin
could give themselves access to every classified item of the organization and get around
need-to-know. Consequences:
- an organization with a single organization admin cannot give that admin a clearance until a
  second organization admin is appointed;
- revoking one's own clearance stays allowed, because it only removes access;
- C8 is enforced in step 3 of #95, with the organization-scoped authorization.

**Decided on 2026-10-23: the guest role is fixed (M3).** The first version of M3 only blocked
Guest → OrgAdmin, which could be bypassed in two steps (Guest → Member → OrgAdmin). Now a guest
membership never changes role, and a member or organization admin never becomes a guest. An
external person who becomes internal goes through ending the membership, which revokes their
clearances (C7), and being added again as a member, so their access is granted again from scratch.

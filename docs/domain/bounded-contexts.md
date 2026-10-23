# Bounded contexts

> **Status: proposed.** This is how the domain splits today, read from the code. When the team
> confirms it, it becomes an ADR. Until then, treat the boundaries as a guide, not as a rule the
> tests enforce.

> **In progress:** ADR-0014 turns organizations into tenants. The `Membership` aggregate (Member,
> Guest, OrgAdmin) exists in Access Governance. Appointing an organization admin, adding a member or
> guest, listing the members, changing a member's role, removing a member and leaving an organization
> are implemented. Policies are managed by the organization's own admins, on organization-scoped
> routes, and no longer by the platform admin (C6 for policies). The organization-scoped authorization
> of clearances comes next (#95); until then the platform admin still manages clearances.
> This page describes the code as it is today.

DataTrustHub is one deployable application with one database and one `DataTrustHub.Domain` project.
The contexts below are **logical** boundaries inside that monolith: they are where the model would
split if the application moved to separate services (ADR-0012).

## The contexts

### 1. Identity and Access

It is about who you are, and whether you are an admin.

| | |
|---|---|
| **Owns** | `User` (email, password hash, role) |
| **Use cases** | register, log in, admin seeding; admin account management (create, list, get, delete) |
| **Code today** | `Features/Authentication`, plus the account slices in `Features/Administration` (`*Account*`) |
| **Publishes** | the user id and the role, carried in the JWT |

### 2. Access Governance

It is about who may see what: the structure of access.

| | |
|---|---|
| **Owns** | `Organization`, `Policy`, `Clearance`, `Membership`, the value objects `ClassificationLevel` and `MembershipRole` |
| **Use cases** | platform admin: create, list, get and delete organizations and clearances, appoint an organization's first organization admin; organization admin: create, list, get and delete the organization's policies, add a member or guest, list the members, change a member's role, remove a member; any member or guest: leave an organization |
| **Code today** | `Features/Administration` (`*Organization*` / `*Group*`, `*Policy*`, `*Clearance*`, `AddMember`, `ListMembers`, `ChangeMemberRole`, `RemoveMember`, `LeaveOrganization`, `Authorization` for the organization admin check, and `MembershipRules` for M2 and C7) |
| **Publishes** | a user's clearances, read by Data Management |

### 3. Data Management

It is about the data itself, and applying the read rule to it.

| | |
|---|---|
| **Owns** | `DataItem` |
| **Use cases** | upload (personal or classified), list, download, delete, list the classification options a user may use |
| **Code today** | `Features/DataManagement` |
| **Consumes** | clearances from Access Governance (`Clearance.Allows`), and the user id from Identity and Access |

### Planned

- **Sharing** (#3): giving another user access to a data item.
- **Audit** (#24): recording who did what, through an audit decorator.

They will each become a context, or a part of one, when they are designed.

## Context map

```
Identity and Access ──(user id)──────────────▶ Access Governance
        │                                              │
        │ (user id, role in JWT)                       │ (clearances)
        ▼                                              ▼
                       Data Management
```

- **Access Governance → Data Management: customer / supplier.** Data Management relies on the
  clearance model to decide reads and classified uploads. A change to `Clearance` or
  `ClassificationLevel` affects Data Management.
- **Identity and Access → the others: upstream.** The other contexts know a user only by id. They
  never load or change a `User`.
- **Shared kernel:** `DataTrustHub.SharedKernel` (`Result`, `Error`, `UserRoles`) is shared by
  every context.

## Rules between contexts

- **Reference by id only.** An aggregate never holds another aggregate as an object. For example,
  `Clearance.UserId` and `DataItem.PolicyId` are ids.
- **One aggregate per command,** as far as possible. Today *delete account* changes four
  aggregates (`User`, its `Clearance`s, its `Membership`s and its `DataItem`s) in one transaction, and
  *remove a member* and *leave an organization* end a `Membership` and revoke `Clearance`s together (C7);
  *delete organization* removes the `Organization` and its `Membership`s together (O3). That is accepted
  inside the monolith. Across services it would become a domain event.
- **Feature folders are not the contexts.** The account slices live in `Administration`, but they
  belong to Identity and Access. Rule 2 of `CLAUDE.md` (no cross-feature imports) still applies.

## Open questions

- Should the account slices move out of `Administration` and into the Identity and Access context?
- Should Access Governance publish an explicit "clearances of user X" port for Data Management,
  instead of sharing the `Clearance` entity? That would let the two contexts split later.

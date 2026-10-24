# Aggregates and invariants

Every aggregate, its value objects and the rules it guarantees. Each rule says **where it is
enforced**:

- **Entity:** an invariant of one aggregate, in an entity method. The ideal place.
- **Use case:** a rule that needs other aggregates, or the set of all instances (uniqueness,
  references, deletion guards). It is checked in the command handler through repositories, because
  one entity cannot see the others. If such a rule grows, it moves to a domain service.
- **Validator:** input validation, i.e. the shape of the request. It is listed when a limit
  matters to the business.

Today every entity is its own aggregate root, with its own repository (`I*Repository`). Aggregates
reference each other **by id only**.

## User

*Context: Identity and Access.* A person with an account. Source: `Domain/Users/User.cs`.

| Attribute | Notes |
|---|---|
| `Id` | |
| `Email` | Used to log in. Unique |
| `HashedPassword` | Never the plain password (`IPasswordHasher`) |
| `Role` | `User` or `Admin` (`UserRoles`) |

**Factory methods:**
- `Register(email, hashedPassword)` creates a `User`.
- `CreateAdmin(email, hashedPassword)` creates an `Admin`.

**Behaviour:**
- `PromoteToAdmin()` returns `false` if the user was already an admin.
- `CheckCanBeDeletedBy(requesterId)`.

| # | Invariant / rule | Enforced in |
|---|---|---|
| U1 | An email belongs to at most one user | Use case: `RegisterUser`, `CreateAccount` (`EmailExistsAsync`) |
| U2 | An email is at most 256 characters; a password is at least 8 | Validator: `RegisterUser` |
| U3 | A user cannot delete their own account, so an admin cannot lock everyone out | Entity: `CheckCanBeDeletedBy` → `Account.CannotDeleteOwnAccount` |
| U4 | Login gives the same error for an unknown email and a wrong password, so it does not reveal which emails exist | Use case: `LoginUser` → `User.InvalidCredentials` |
| U5′ | Deleting a user also removes their clearances and memberships and soft-deletes their personal data items, all in one transaction. Their classified data items are not touched: they stay with their organization, whose organization admins can delete them (ADR-0014) | Use case: `DeleteAccount` |
| U6 | Startup makes sure the configured admin exists, and promotes that user if needed | Use case: `AdminSeeder` |

## Organization

*Context: Access Governance.* The body that policies belong to. Source:
`Domain/Organizations/Organization.cs`.

| Attribute | Notes |
|---|---|
| `Id` | |
| `Name` | Unique |

**Factory method:** `Create(name)`.

| # | Invariant / rule | Enforced in |
|---|---|---|
| O1 | Organization names are unique | Use case: `CreateOrganization` (`NameExistsAsync`) |
| O2 | An organization that still has policies cannot be deleted | Use case: `DeleteOrganization` (`AnyInOrganizationAsync`) |
| O3 | Deleting an organization also removes its memberships, in the same transaction | Use case: `DeleteOrganization` (`ListForOrganizationAsync`) |

## Policy

*Context: Access Governance.* A named area of access inside one organization. Source:
`Domain/Policies/Policy.cs`.

| Attribute | Notes |
|---|---|
| `Id` | |
| `Name` | Unique within its organization |
| `OrganizationId` | The owning organization (by id) |

**Factory method:** `Create(name, organizationId)`.

| # | Invariant / rule | Enforced in |
|---|---|---|
| P1 | A policy belongs to an existing organization | Use case: `CreatePolicy`. The organization comes from the route, and the caller must be an organization admin there (C6); deleting an organization ends its memberships (O3), so that check also proves the organization exists (`Organization.NotFound` otherwise) |
| P2 | Policy names are unique within an organization | Use case: `CreatePolicy` (`NameExistsInOrganizationAsync`) → `Policy.NameAlreadyInUse` (409) |
| P3 | A policy on which clearances are still granted cannot be deleted | Use case: `DeletePolicy` (`AnyForPolicyAsync`) → `Policy.HasClearances` (409) |
| C6 (policies) | Only an organization admin of X creates, lists, reads and deletes X's policies. The platform admin does not manage policies (ADR-0014) | Use case + authorization: `CreatePolicy`, `ListPolicies`, `GetPolicy`, `DeletePolicy` on `/organizations/{organizationId}/policies`, checked first (`IsOrgAdminAsync`) → `Organization.NotFound` (404) for anyone else, the platform admin included. A policy of another organization answers `Policy.NotFound` (404), so it is not revealed. For clearances, see C6 (clearances) |

## Clearance

*Context: Access Governance.* Grants a user one classification level on one policy. Source:
`Domain/Clearances/Clearance.cs`.

| Attribute | Notes |
|---|---|
| `Id`, `Name` | |
| `UserId` | The cleared user (by id) |
| `PolicyId` | The policy (by id) |
| `Level` | A `ClassificationLevel` |

**Factory method:** `Grant(name, userId, policyId, level)`.

**Behaviour:** `Allows(policyId, priority)` is true if the clearance is on that policy and its
level's priority is at least `priority`.

| # | Invariant / rule | Enforced in |
|---|---|---|
| C1 | A clearance covers its own level and every lower level, on its policy only | Entity: `Allows` |
| C2 | A clearance references an existing user and an existing policy of the organization | Use case: `CreateClearance` (`IPolicyRepository.GetByIdAsync` and the policy's `OrganizationId`, `IUserRepository.ExistsAsync`) → `Clearance.PolicyNotFound` (404, also for a policy of another organization, so it is not revealed), `Clearance.UserNotFound` (404) |
| C3 | A user holds at most one clearance per policy | Use case: `CreateClearance` (`IClearanceRepository.ExistsAsync`) → `Clearance.AlreadyGranted` (409) |
| C4 | A level's priority is never negative | Validator: `CreateClearance` |
| C5 | A clearance on a policy of organization X is granted only to a user with a membership in X: a member, a guest or an organization admin (ADR-0014) | Use case: `CreateClearance` (`IMembershipRepository.ExistsAsync`) → `Clearance.UserNotMember` (409) |
| C6 (clearances) | Only an organization admin of X grants, lists, reads and revokes clearances on X's policies. The platform admin does not manage clearances (ADR-0014) | Use case + authorization: `CreateClearance`, `ListClearances`, `GetClearance`, `DeleteClearance` on `/organizations/{organizationId}/clearances`, checked first (`IsOrgAdminAsync`) → `Organization.NotFound` (404) for anyone else, the platform admin included. A clearance on a policy of another organization answers `Clearance.NotFound` (404), and the list shows only clearances on the organization's policies, so neither is revealed |
| C7 | Ending a membership in an organization revokes every clearance of that user on the organization's policies, in the same transaction; clearances on other organizations' policies stay (ADR-0014) | Use case: `RemoveMember`, `LeaveOrganization` (`ClearanceRevocation`, through `IPolicyRepository.ListIdsInOrganizationAsync`); `DeleteAccount` removes all the user's clearances |
| C8 | An organization admin cannot grant a clearance to themselves (four-eyes); another organization admin of the same organization must grant it. Revoking one's own clearance is allowed, because it only removes access (ADR-0014) | Use case: `CreateClearance` (the user is the caller) → `Clearance.CannotGrantSelf` (409) |

**Grant a clearance** (`CreateClearance`, an organization admin of the organization) checks, in this
order: the caller is an organization admin there (C6, `Organization.NotFound`), the policy exists and
belongs to the organization (C2, `Clearance.PolicyNotFound`), the user exists (C2,
`Clearance.UserNotFound`), the user has a membership there (C5, `Clearance.UserNotMember`), the user is
not the caller (C8, `Clearance.CannotGrantSelf`), and the user holds no clearance on the policy yet (C3,
`Clearance.AlreadyGranted`). An organization admin may hold a clearance, but another organization admin
must grant it; so an organization with a single organization admin cannot clear that admin.

**Revoke a clearance** (`DeleteClearance`, an organization admin of the organization) removes a
clearance on one of the organization's policies; the classification level stays, because other
clearances may share it.

### Value object: ClassificationLevel

Source: `Domain/Clearances/ClassificationLevel.cs`. It is an immutable `record (Name, Priority)`.
Access compares priorities only.

The standard levels are `ClassificationLevel.Standard`, used for data items:

| Priority | Name |
|---|---|
| 0 | Public |
| 1 | Internal |
| 2 | Confidential |
| 3 | Secret |
| 4 | Top Secret |

## Membership

*Context: Access Governance.* A user's membership in one organization, with a role there
(ADR-0014). Source: `Domain/Memberships/Membership.cs`.

It is its own aggregate, not part of `Organization`: loading every member to change one would make
each command heavy, and concurrent edits would conflict. So M1 and M2 are checked in the use case.

| Attribute | Notes |
|---|---|
| `Id` | |
| `UserId` | The member (by id) |
| `OrganizationId` | The organization (by id) |
| `Role` | A `MembershipRole`: `Member`, `Guest` or `OrgAdmin` |

**Factory methods:**
- `AddMember(userId, organizationId)`
- `AddGuest(userId, organizationId)`
- `AppointOrgAdmin(userId, organizationId)`

**Behaviour:** `ChangeRole(newRole)` returns a `Result`. Changing to the same role succeeds and
changes nothing. A member and an organization admin can swap roles; the guest role never changes
(M3).

| # | Invariant / rule | Enforced in |
|---|---|---|
| M1 | A user has at most one membership per organization | Use case: `AddMember` (`ExistsAsync`) → `Membership.AlreadyMember` (409); `AppointOrganizationAdmin` reuses an existing membership (`GetAsync`). Database: unique index on (`UserId`, `OrganizationId`); when two concurrent requests both pass the check, the save fails with `UniqueConstraintViolationException`, and `AddMember` and `AppointOrganizationAdmin` answer `Membership.AlreadyMember` too (for appoint, a retry gets `Organization.AlreadyHasOrgAdmin`) |
| M2 | An organization always has at least one organization admin, so the last one cannot be removed, demoted or leave | Use case: `ChangeMemberRole`, `RemoveMember`, `LeaveOrganization` (`OrgAdminRule`, `CountOrgAdminsAsync`) → `Membership.LastOrgAdmin` (409). Not `DeleteAccount`: an organization that loses its last organization admin that way gets one again from the platform admin. The migration appoints every platform admin as organization admin of every existing organization |
| M3 | The guest role is fixed: a guest cannot change to another role, and a member or organization admin cannot become a guest. To change it, the membership is ended and the person is added again, so their access is granted again from scratch | Entity: `ChangeRole` → `Membership.GuestRoleIsFixed` (409), e.g. when `AppointOrganizationAdmin` is called for a guest |

**Appoint an organization admin** (`AppointOrganizationAdmin`, platform admin only) checks, in this
order: the organization exists (`Organization.NotFound`), the user exists (`Membership.UserNotFound`),
the user is not the caller (`Membership.CannotAppointSelf`, 409), and the organization has no
organization admin yet (`CountOrgAdminsAsync`, `Organization.AlreadyHasOrgAdmin`, 409). So the platform
admin appoints only the first organization admin, and never themselves; the others are appointed
by the organization's own admins, by changing a member's role. Then a non-member gets a new `OrgAdmin` membership, a member's role is
changed, and a guest is refused (M3).

**Change a member's role** (`ChangeMemberRole`, an organization admin of the organization) checks, in
this order: the caller is an organization admin there (`Organization.NotFound` otherwise, so outsiders
learn nothing), the user has a membership there (`Membership.NotFound`), and, when the role really
changes, that an organization admin being demoted is not the last one (M2, `Membership.LastOrgAdmin`).
The new role is `Member` or `OrgAdmin`; a guest's membership is refused by the entity (M3). The same
role changes nothing. An organization admin may demote themselves while another one remains.

**Remove a member** (`RemoveMember`, an organization admin) and **leave an organization**
(`LeaveOrganization`, the caller, any role) end a membership: the last organization admin is refused
(M2), and the user's clearances on the organization's policies are revoked in the same save (C7). A
caller with no membership in the organization gets `Organization.NotFound` on leave. Leaving is how the
platform admins that the migration made organization admins leave those organizations (ADR-0014).

### Value object: MembershipRole

Source: `Domain/Memberships/MembershipRole.cs`. It is an immutable `record (Name)`, stored by name.
The standard roles are `MembershipRole.Standard`; `FindStandard(name)` returns null for an unknown
name.

| Name | Meaning |
|---|---|
| `Member` | Holds clearances, reads and uploads data |
| `Guest` | An external person, added explicitly; works like a member, but never manages the organization. The role is fixed (M3) |
| `OrgAdmin` | Manages the organization: its members, policies and clearances |

## DataItem

*Context: Data Management.* A stored file. Source: `Domain/DataItems/DataItem.cs`.

| Attribute | Notes |
|---|---|
| `Id`, `Name`, `Size`, `Content` | `Content` is the file as base64 |
| `OwnerUserId` | The user who uploaded it (by id) |
| `SecurityMarking` | `UNCLASSIFIED` for personal items, otherwise the level name |
| `PolicyId`, `ClassificationPriority` | Both set for a classified item, both empty for a personal one |
| `IsDeleted` | Soft delete |

**Factory methods:**
- `CreatePersonal(…)`
- `CreateClassified(…, policyId, level, ownerClearances)`, which returns a `Result`.

**Behaviour:**
- `IsReadableBy(userId, userClearances)`
- `MarkDeleted(requesterId, requesterRights)`. `DeletionRights` carries what the item cannot know: whether its
  organization still exists, and whether the requester is a member or an organization admin there

| # | Invariant / rule | Enforced in |
|---|---|---|
| D1 | A data item is either personal (no policy, no level) or classified (policy and a standard level together). A partial or unknown classification is rejected, never downgraded to personal | Entity: the two factory methods. Use case: `AddDataItem` (`DataItem.IncompleteClassification`, `DataItem.UnknownClassificationLevel`), and Validator |
| D2 | An owner may classify only under a policy they hold a clearance on, and at most at that clearance's level | Entity: `CreateClassified` → `DataItem.NotCleared` (400) |
| D3′ | **Read rule:** a classified item is readable only by a holder of a clearance on its policy at or above its level; its owner has no special right on it. A personal item is readable only by its owner. A deleted item is readable by no one (ADR-0014) | Entity: `IsReadableBy`. Its SQL form is `DataAccess.VisibleTo`, used by list and download. `ReadRuleEquivalenceTests` proves both forms agree (#99) |
| D4 | **Need-to-know:** admins have no bypass. An unreadable item answers 404 (`DataItem.NotFound`), so its existence is not revealed | Entity (D3′) and use cases `GetDataItem`, `ListDataItems` |
| D5′ | **Delete rule:** a personal item is deleted by its owner only. A classified item belongs to its organization (its policy's organization): it is deleted by its owner while they are a member of that organization (any role), or by an organization admin of that organization, even one who cannot read it. A classified item whose policy no longer exists is deleted by its owner only. Anyone else, or deleting twice, gets 404, so the item's existence is not revealed (ADR-0014) | Entity: `MarkDeleted`. Use case: `DeleteDataItem` gathers the requester's rights (`IPolicyRepository`, `IMembershipRepository`). Its SQL form is `DataAccess.DeletableBy`, used for the `canDelete` flag of list and download |
| D6 | A file is required and is at most 100 MB. Its name is at most 512 characters | Use case: `AddDataItem` (`DataItem.NoFileProvided`, `DataItem.FileTooLarge`), and Validator |
| D7 | The owner must be an existing user | Use case: `AddDataItem` (`DataItem.UserNotFound`) |

## AuditRecord

*Cross-cutting: the audit trail (ADR-0016).* One entry of the audit trail: who did what, on which
target, in which organization, with which outcome. Source: `Domain/Auditing/AuditRecord.cs`. It is not
loaded or changed by any use case; it is only appended through the `IAuditLog` port.

| Attribute | Notes |
|---|---|
| `Id` | |
| `OccurredAt` | When the command or access completed, in UTC |
| `ActorUserId` | The user who did it (by id); empty for an anonymous action (register, log in) |
| `Action` | The command name without the `Command` suffix, e.g. `DeleteDataItem` |
| `TargetType`, `TargetId` | What the action was on, e.g. `DataItem` and its id; both set or both empty |
| `OrganizationId` | The organization the action ran in, when there is one (by id) |
| `Outcome` | An `AuditOutcome`: `Succeeded`, `Rejected`, `Denied` or `Failed`, stored by name |
| `ErrorCode` | The error code of a `Rejected` or `Denied` action, the exception type name of a `Failed` one |

**Factory method:** `Create(occurredAt, actorUserId, action, targetType, targetId, organizationId,
outcome, errorCode)`. Its arguments come from code, not from user input, so a broken rule throws.

| # | Invariant / rule | Enforced in |
|---|---|---|
| A1 | An audit record has an action, a UTC time, a target type and target id together or neither, and an error code only when the outcome is not `Succeeded` | Entity: `Create` |
| A2 | It holds nothing else: never a command's payload, file content, emails, passwords, hashes or tokens. Login and register are recorded anonymous, with the outcome only | Entity (no such attributes) and `AuditCommandDecorator` |
| A3 | **Append-only:** no use case updates or deletes an audit record | Port: `IAuditLog` has `AppendAsync` only |
| A4 | Every command leaves a record, also when validation or a domain rule rejects it, or when it throws; queries are not audited | `AuditCommandDecorator`, the outermost decorator of every command handler. An exception is recorded as `Failed` and rethrown unchanged; a failed audit write is logged and does not change the command's answer |
| A5 | The trail survives a failed command: the record is saved on its own, after the command, never in the command's unit of work | Adapter: `AuditLog` writes through its own short-lived `DContext` |
| A6 | No foreign keys: a record outlives the users, organizations and data items it names | Database: table `AuditRecords`, indexed on `OccurredAt` and on (`OrganizationId`, `OccurredAt`) |

The organization and the target come from the command, when it declares them (`IOrganizationScopedCommand`,
`ITargetedCommand`). When only the use case learns them while it runs, its handler fills the request's
`AuditScope`, which wins: `AddDataItem` sets the new item and, for a classified item, its organization;
`DeleteDataItem` sets the organization of a classified item whose policy exists.

## Known gaps

These are findings, not decisions. Each one needs an issue before it changes.

- **Clearance levels are free-form** (C4): any name, and any priority from 0 upwards. Data items,
  however, use only the standard levels. Should a clearance accept only `ClassificationLevel.Standard`?
- **Deleting a policy ignores its data items** (P3 checks clearances only). Classified items under a
  deleted policy become readable by no one, because no clearance on that policy remains (D3′). They
  have no organization any more, so no organization admin can delete them and membership cannot be
  checked: only their owner can delete them (D5′), by id through the API, since no page lists them.
- **An organization admin without a clearance cannot see what they may delete** (D5′). They can delete
  a classified item of their organization only by its id through the API: the WebApp lists readable
  items only (D3′), because the names of unreadable items can be sensitive.
- **Two error codes for one rule** (U1): `User.EmailAlreadyInUse` on register and
  `Account.EmailAlreadyInUse` on admin create.
- **M2 is checked, not locked:** the last-organization-admin rule is checked in the use case
  (`CountOrgAdminsAsync`). Two organization admins who demote or remove each other, or leave, at the
  same moment can both pass the count and leave the organization with none. The platform admin can
  then appoint one again.
- **Uniqueness is checked, not constrained:** U1, O1 and P2 are checked before saving. Two concurrent
  requests can both pass unless the database has a unique index.

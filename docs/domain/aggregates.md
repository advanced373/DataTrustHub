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
| U5 | Deleting a user also removes their clearances and soft-deletes the data items they own, all in one transaction | Use case: `DeleteAccount` |
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
| P1 | A policy belongs to an existing organization | Use case: `CreatePolicy` (`Policy.OrganizationNotFound`) |
| P2 | Policy names are unique within an organization | Use case: `CreatePolicy` (`NameExistsInOrganizationAsync`) |
| P3 | A policy on which clearances are still granted cannot be deleted | Use case: `DeletePolicy` (`AnyForPolicyAsync`) |

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
| C2 | A clearance references an existing user and an existing policy | Use case: `CreateClearance` (`Clearance.UserNotFound`, `Clearance.PolicyNotFound`) |
| C3 | A user holds at most one clearance per policy | Use case: `CreateClearance` (`Clearance.AlreadyGranted`) |
| C4 | A level's priority is never negative | Validator: `CreateClearance` |

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
- `MarkDeleted(requesterId)`

| # | Invariant / rule | Enforced in |
|---|---|---|
| D1 | A data item is either personal (no policy, no level) or classified (policy and level together) | Entity: the two factory methods |
| D2 | An owner may classify only under a policy they hold a clearance on, and at most at that clearance's level | Entity: `CreateClassified` → `DataItem.NotCleared` (400) |
| D3 | **Read rule:** readable by its owner, or by a holder of a clearance on its policy at or above its level. A deleted item is readable by no one | Entity: `IsReadableBy`. Its SQL form is `DataAccess.VisibleTo`, used by list and download |
| D4 | **Need-to-know:** admins have no bypass. An unreadable item answers 404 (`DataItem.NotFound`), so its existence is not revealed | Entity (D3) and use cases `GetDataItem`, `ListDataItems` |
| D5 | Only the owner deletes. Deleting someone else's item, or deleting twice, answers 404 | Entity: `MarkDeleted` |
| D6 | A file is required and is at most 100 MB. Its name is at most 512 characters | Use case: `AddDataItem` (`DataItem.NoFileProvided`, `DataItem.FileTooLarge`), and Validator |
| D7 | The owner must be an existing user | Use case: `AddDataItem` (`DataItem.UserNotFound`) |

## Known gaps

These are findings, not decisions. Each one needs an issue before it changes.

- **D3 exists in two forms:** the entity method and the SQL predicate. A test that keeps them equal is
  planned in ADR-0012 step 4 (#74).
- **Clearance levels are free-form** (C4): any name, and any priority from 0 upwards. Data items,
  however, use only the standard levels. Should a clearance accept only `ClassificationLevel.Standard`?
- **Partial classification on upload:** a policy without a level, or an unknown level, silently
  creates a personal item (#84).
- **Deleting a policy ignores its data items** (P3 checks clearances only). Classified items under a
  deleted policy become readable by their owner only.
- **Two error codes for one rule** (U1): `User.EmailAlreadyInUse` on register and
  `Account.EmailAlreadyInUse` on admin create.
- **Uniqueness is checked, not constrained:** U1, O1 and P2 are checked before saving. Two concurrent
  requests can both pass unless the database has a unique index.

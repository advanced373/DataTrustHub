# Ubiquitous language

Part of the [domain handbook](README.md). This is the shared vocabulary of DataTrustHub, as DDD uses the term *ubiquitous language*. Start here
if you are new to the project. Everyone uses these words: users of the product, developers, the AI
agents (`.claude/agents/`), and the issues, PRs, code names and comments.

- If a term is missing, or the code and this page disagree, raise it. Don't invent a synonym.
- Add every new term here, in the same PR that introduces it.

## The business in one page

DataTrustHub is a **data lake with access control**. Today it holds documents only. A user can open
a document only if the rules say so.

The access rules:

1. People have accounts (**users**). An **organization** is a tenant, i.e. a company on the platform.
   A user belongs to an organization through a **membership**, as a member, a guest or an
   organization admin (ADR-0014). The platform admin appoints an organization's first organization
   admin; an organization admin adds members and guests, lists them, changes their roles and removes
   them, and any member or guest can leave. Ending a membership revokes the user's clearances in that
   organization (#95).
2. An organization defines **policies**: named areas of access, such as "Project Apollo" or "HR".
   Its organization admins create and delete them.
3. A **data item** (an uploaded document) is either **personal** or **classified**.
   - A **personal** data item is private to the user who uploaded it.
   - A **classified** data item is placed under one policy, at one **classification level**, from
     *Public* up to *Top Secret*.
4. An organization admin gives a member, guest or organization admin of the organization a
   **clearance**: access to one of its policies, up to one classification level. An organization admin
   never grants a clearance to themselves; another organization admin does.
5. A user may read a classified data item only if they hold a clearance on its policy at that level or
   higher. The owner has no special right on it: classified data belongs to the organization. Only the
   owner reads a personal data item (ADR-0014).
6. **Need-to-know:** being an admin does not give access to data. If you may not read a data item,
   the system answers as if it did not exist.

Example: Ana has a *Secret* clearance on "Project Apollo".
- She can read Apollo documents marked *Confidential* and *Secret*, but not *Top Secret*.
- She cannot read HR documents at any level.

The rules are recorded in ADR-0011; ADR-0014 changes the read rule for classified data. The
architecture that implements them is in ADR-0012 and [the architecture overview](../architecture/overview.md).

## Domain terms

| Term | Meaning | In code |
|---|---|---|
| **User** | A person with an account. Their role is `User` or `Admin` | `User` |
| **Admin** | A user who manages organizations and users; policies and clearances are now managed by organization admins. Admins have no automatic access to data (need-to-know). Under ADR-0014 this role becomes the *platform admin* | `User.Role` |
| **Platform admin** | A user with the `Admin` role. Under ADR-0014 they manage tenants only: create and delete organizations and appoint their first organization admin. Not to be confused with an organization admin | `UserRoles.Admin`, `User.IsAdmin` |
| **Membership** | A user's membership in one organization, with a role there. A user can be a member of several organizations, with at most one membership in each | `Membership` |
| **Membership role** | The role of a membership: member, guest or organization admin | `MembershipRole` |
| **Member** | A membership role: holds clearances, reads and uploads data in the organization | `MembershipRole.Member` |
| **Guest** | A membership role for an external person, added explicitly. Works like a member, but never manages the organization. The guest role is fixed: a guest never changes role, and no member or organization admin becomes a guest; the membership is ended and the person added again instead | `MembershipRole.Guest` |
| **Organization admin** | A membership role that manages one organization: its members, policies and clearances. Write *OrgAdmin* in code only | `MembershipRole.OrgAdmin` |
| **Appoint an organization admin** | The platform admin makes a user the first organization admin of one organization: a new membership, or a member's role changed. Only the first one: when the organization already has an organization admin, its own admins appoint the others, by changing a member's role. The platform admin never appoints themselves, and a guest cannot be appointed (the guest role is fixed). It gives the platform admin no access to the organization | `AppointOrganizationAdmin`, `Membership.AppointOrgAdmin` |
| **Add a member** | An organization admin adds a user to their organization as a member or a guest. A user already in the organization cannot be added again | `AddMember`, `Membership.AddMember`, `Membership.AddGuest` |
| **Change a member's role** | An organization admin makes a member an organization admin, or an organization admin a member; this is how further organization admins are appointed. The guest role is fixed, and the last organization admin cannot be demoted | `ChangeMemberRole`, `Membership.ChangeRole` |
| **Remove a member** | An organization admin ends another user's membership in their organization. It revokes the user's clearances on the organization's policies. The last organization admin cannot be removed | `RemoveMember` |
| **Leave an organization** | A member, guest or organization admin ends their own membership. It revokes their clearances on the organization's policies. The last organization admin cannot leave | `LeaveOrganization` |
| **My organizations** | The organizations the signed-in user belongs to, each with the role of their membership there. A user sees only their own memberships | `ListMyOrganizations` |
| **Find a user by email** | An organization admin looks up a user by their exact email address, to add them as a member or guest. Only an exact match: an organization admin can confirm an address they already know, but cannot list or search the users of the platform. The answer says whether the user already has a membership in the organization, and its role | `FindUserByEmail` |
| **Organization** | A tenant: the company that policies, clearances and classified data belong to. The word is never *group*. The old *Group* names in the API and the UI are being renamed (#91) | `Organization` |
| **Policy** | A named area of access inside one organization. Every classified data item belongs to one policy | `Policy` |
| **Classification level** | A named sensitivity level with a priority: Public (0), Internal, Confidential, Secret, Top Secret (4) | `ClassificationLevel` |
| **Clearance** | Grants a user access to one policy at one classification level, and to every level below it | `Clearance`, `Clearance.Allows` |
| **Grant a clearance** | An organization admin gives a clearance on one of the organization's policies to a member, guest or organization admin of that organization. Never to themselves: another organization admin of the organization must grant it (four-eyes). A user holds at most one clearance per policy | `CreateClearance`, `Clearance.Grant` |
| **Revoke a clearance** | An organization admin removes a clearance on one of the organization's policies. Revoking their own clearance is allowed, because it only removes access. Ending a membership also revokes the user's clearances there | `DeleteClearance` |
| **Data item** | A stored file. Today it is always a document | `DataItem` |
| **Owner** | The user who uploaded a data item. Don't use *uploader* in code | `DataItem.OwnerUserId` |
| **Personal data item** | A data item with no policy, readable by its owner only. Its marking is `UNCLASSIFIED` | `DataItem.CreatePersonal` |
| **Classified data item** | A data item with a policy and a classification level | `DataItem.CreateClassified` |
| **Security marking** | The label shown on a data item, e.g. `SECRET` | `DataItem.SecurityMarking` |
| **Read rule** | A user may read a classified data item only if they hold a clearance on its policy at or above its level; its owner has no special right on it. A personal data item is read by its owner only. A deleted data item is read by no one (ADR-0014, D3′) | `DataItem.IsReadableBy` |
| **Delete rule** | A personal data item is deleted by its owner only. A classified data item belongs to its organization (its policy's organization): it is deleted by its owner while they are a member there (any role), or by an organization admin there, even one who cannot read it. A classified data item whose policy no longer exists is deleted by its owner only (ADR-0014, D5′) | `DataItem.MarkDeleted`, `DeletionRights` |
| **Need-to-know** | An admin cannot bypass the read rule. A data item the user may not read answers 404, so its existence is not revealed | — |
| **Audit trail** | The append-only record of who did what: every command, also a rejected or failed one (ADR-0016). Nothing in it is updated or deleted | `IAuditLog`, `AuditCommandDecorator` |
| **Audit record** | One entry of the audit trail: when, who (or anonymous), the action, the target, the organization and the outcome. Never a payload, file content, email, password, hash or token | `AuditRecord` |
| **Outcome** | How an audited action ended: *Succeeded*; *Rejected* by validation or a domain error, with its error code; *Denied*, a read the user may not make; *Failed*, an exception | `AuditOutcome` |
| **Tenant-level action** | One of the platform's own use cases: `CreateOrganization`, `DeleteOrganization`, `AppointOrganizationAdmin`, `CreateAccount`, `DeleteAccount`, `RegisterUser`, `LoginUser`. Their records are the only ones the platform admin sees in the audit trail. An action inside an organization (members, policies, clearances, data items) is not tenant-level | `TenantLevelActions` (`ListAuditRecords`) |

## Building blocks (DDD / Clean Architecture)

These are the words for how the code is structured (ADR-0012).

| Term | In this project |
|---|---|
| **Domain layer** | `DataTrustHub.Domain`: entities, value objects, domain errors and ports. It has no EF Core and does not reference Infrastructure |
| **Application layer** | `DataTrustHub.Features`: one **use case** per vertical slice, as a command handler or a query handler |
| **Infrastructure layer** | `DataTrustHub.Infrastructure`: the adapters (repositories, mappers, `DContext`) |
| **Presentation layer** | `DataTrustHub.API` (Carter endpoints) and the WebApp |
| **Entity** | A domain object with an identity and behaviour: `User`, `Organization`, `Policy`, `Clearance`, `Membership`, `DataItem`, and `AuditRecord`, which is only appended |
| **Aggregate root** | An entity that is loaded and saved as a whole, through its own repository. Today every entity is its own aggregate root |
| **Value object** | An immutable object defined by its values: `ClassificationLevel`, `MembershipRole`, `DeletionRights` |
| **Invariant / business rule** | A rule that must always hold. A rule of one aggregate lives in an entity method (`IsReadableBy`, `MarkDeleted`, `CheckCanBeDeletedBy`). A rule that needs other aggregates or the whole set (uniqueness, references, deletion guards) is checked in the use case through repositories. Never in validators. The full list is in [aggregates.md](aggregates.md) |
| **Factory method** | The only way to create a new entity. It applies the entity's rules: `User.Register`, `Policy.Create`, `Clearance.Grant`, `DataItem.CreateClassified` |
| **Rehydration** | Rebuilding a stored entity without running its creation rules, through an `internal` constructor that only the persistence mappers can see |
| **Domain error** | An expected rule failure, returned as a `Result` instead of thrown: `UserErrors`, `DataItemErrors`, `MembershipErrors` |
| **Port** | An interface the domain or application layer needs, implemented outside it: `I*Repository`, `IUnitOfWork`, `IPasswordHasher`, `IAuditLog` |
| **Adapter** | The Infrastructure implementation of a port |
| **Repository** | A port that loads and stores one aggregate as an entity. Only commands use repositories |
| **Unit of work** | A port that commits all changes of one command in a single transaction: `IUnitOfWork`, implemented by `DContext` |
| **Command / command handler** | A use case that changes state. It loads aggregates, calls their behaviour and saves once (`ICommandHandler`) |
| **Query / query handler** | A use case that only reads (`IQueryHandler`). It uses read models, not aggregates (the CQRS read side, ADR-0012 step 4) |
| **Read model** | A flat shape built for display, served by an `I*Queries` port |
| **Persistence model** | The shape of an EF table, `Db*` (`DbUser`, `DbDataItem`). It exists only in Infrastructure. Never call it an entity |
| **Mapper** | Converts between a persistence model and an entity (Mapperly). It lives in Infrastructure |
| **Input validation** | Checks on the shape of a request (required fields, length, format), in the slice's `*Validator`. Business rules do not go here |
| **Decorator** | A cross-cutting step that Scrutor wraps around handlers: validation, and auditing around every command handler (outermost) |
| **Domain service** | A rule that needs several aggregates and belongs to none of them. There are none yet |
| **Bounded context** | A part of the domain with its own model and language. Today there are three: Identity and Access, Access Governance and Data Management ([bounded-contexts.md](bounded-contexts.md)) |

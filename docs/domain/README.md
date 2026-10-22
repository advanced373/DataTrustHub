# Domain handbook

This handbook describes **what** DataTrustHub models and **which rules** it guarantees. It is
written in DDD terms. The architecture overview covers the other side: how the code is built.

New to the project? Read the pages in this order:

1. **The model at a glance** (below): the concepts and the map of what exists.
2. [Ubiquitous language](ubiquitous-language.md): the business in one page, and the vocabulary everyone uses.
3. [Bounded contexts](bounded-contexts.md): the parts of the domain, what each owns, and how they relate.
4. [Aggregates and invariants](aggregates.md): every entity, value object and rule, and where each
   rule is enforced.

## The model at a glance

### The four concepts, in one sentence each

| Concept | What it is | Question it answers |
|---|---|---|
| **Bounded context** | A part of the business with its own model and words. It is a boundary around several aggregates | *Which part of the business is this?* |
| **Aggregate** | A group of objects that changes together and is saved together, in one transaction. One entity in it is the **aggregate root**: the only entry point from outside | *What do I load, change and save as one unit?* |
| **Entity** | An object with an identity (`Id`) that lives and changes over time. Two entities with the same data are still two different things | *Is this a specific "one" that I follow over time?* |
| **Value object** | An object defined only by its values, with no `Id`, and immutable. Two with the same values are the same thing | *Does only the value matter?* |

They nest like this: a **bounded context** contains **aggregates**. An **aggregate** has one
**root entity** and can contain other entities and **value objects**.

### What DataTrustHub has

```
Identity and Access                     (bounded context)
└── User aggregate
    └── User                            (entity, aggregate root)

Access Governance                       (bounded context)
├── Organization aggregate
│   └── Organization                    (entity, aggregate root)
├── Policy aggregate
│   └── Policy                          (entity, aggregate root)  ──▶ OrganizationId
└── Clearance aggregate
    ├── Clearance                       (entity, aggregate root)  ──▶ UserId, PolicyId
    └── ClassificationLevel             (value object)

Data Management                         (bounded context)
└── DataItem aggregate
    └── DataItem                        (entity, aggregate root)  ──▶ OwnerUserId, PolicyId
        uses ClassificationLevel        (value object, from Access Governance)
```

`──▶` is a reference **by id** to another aggregate. It is not an object held inside this one.

| Bounded context | Aggregate | Aggregate root (entity) | Value objects | Repository |
|---|---|---|---|---|
| Identity and Access | User | `User` | — | `IUserRepository` |
| Access Governance | Organization | `Organization` | — | `IOrganizationRepository` |
| Access Governance | Policy | `Policy` | — | `IPolicyRepository` |
| Access Governance | Clearance | `Clearance` | `ClassificationLevel` | `IClearanceRepository` |
| Data Management | DataItem | `DataItem` | uses `ClassificationLevel` when classifying | `IDataItemRepository` |

So today:
- There are **5 entities**, and each is the root of its own aggregate (**5 aggregates**). None of
  them contains another entity.
- There is **1 value object**: `ClassificationLevel`.
- There are **3 bounded contexts**. They are still a proposal; see
  [bounded-contexts.md](bounded-contexts.md).

### Why Policy is not inside the Organization aggregate

A policy is created, deleted and granted on its own, and nothing about an organization has to
change in the same transaction. So it is its own aggregate and points to its organization by
`OrganizationId`. The same reasoning applies to `Clearance` and its user and policy.

The cross-aggregate rule "an organization with policies cannot be deleted" is therefore checked in
the use case (rule O2 in [aggregates.md](aggregates.md)).

### Candidates, not modelled yet

These would become value objects if they grew rules:
- `Email` (format, normalisation);
- `SecurityMarking`;
- `FileContent` (size limit).

Today they are plain strings or numbers.

## Keeping it true

The handbook must match the code. A PR that changes a domain rule updates the handbook in the same
PR. Such changes include:
- an entity or value object;
- an invariant;
- a domain error;
- a bounded context.

The reviewer agent checks this.

Each rule names the place that enforces it (an entity method, a use case or a validator), so it can
be checked against the code.

Related decisions:
- ADR-0011: the access model (policies, classification levels, clearances, need-to-know).
- ADR-0012: Clean Architecture with vertical slices.

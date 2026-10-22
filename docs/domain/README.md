# Domain handbook

This handbook describes **what** DataTrustHub models and **which rules** it guarantees. It is
written in DDD terms. The architecture overview covers the other side: how the code is built.

New to the project? Read the pages in this order:

1. [Ubiquitous language](ubiquitous-language.md): the business in one page, and the vocabulary everyone uses.
2. [Bounded contexts](bounded-contexts.md): the parts of the domain, what each owns, and how they relate.
3. [Aggregates and invariants](aggregates.md): every entity, value object and rule, and where each
   rule is enforced.

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

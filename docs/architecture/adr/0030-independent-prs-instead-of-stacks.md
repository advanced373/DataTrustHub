# ADR-0030: Every PR is independent and targets `develop`; no stacked PRs

- **Status:** Accepted
- **Date:** 2026-11-10
- **Related:** supersedes the stacking part of [ADR-0009](0009-gitflow-and-small-stacked-prs.md); #187, #189, #192, #193, #194, #196, #197

## Context

ADR-0009 split larger work into stacks: each PR targeted the previous PR's branch. Stacks must be merged
bottom-up. When a PR higher in the stack merges first, it lands in its parent's branch, not in `develop`,
and the parent PR stays open.

This happened again and again: #52–#77 in October needed six catch-up PRs. On 2026-11-10 #189 merged
into #187's branch, and #193, #194, #196 and #197 merged into #192's branch. Automatic deletion of head
branches only retargets a dependent PR when its base merges first, so it does not prevent this.

Stacked PRs also cannot be reviewed or merged on their own. Each one depends on another.

## Decision

- **Every PR branches from `develop` and targets `develop`.** A PR's base is never another feature branch.
- **Every open PR can be merged in any order.** Each one builds, passes the tests and does not conflict
  with the other open PRs.
- **Split larger work into independent slices**, for example one use case or one feature folder per PR.
  The PR size limit of ADR-0009 (about 100–350 changed lines) stays.
- **Shared groundwork goes first, on its own.** A domain method, a port or a shared type that several
  slices need gets its own PR. The slices that need it are opened only after it is merged, from an
  updated `develop`.
- **When B needs A, B waits.** B may be written locally, but it is opened as a PR only after A is merged
  and B is rebased on `develop`.
- **At most one open PR adds an EF migration at a time**, so the model snapshot stays linear.
- ADRs and other docs stay in their own PRs. Code PRs must not depend on them to build.

## Alternatives considered

- **Keep stacks and retarget after each merge.** Already tried. Merging out of order kept happening, and
  every time it needed catch-up PRs.
- **Stacking tools (Graphite, merge queues).** They add tooling and process for a one-person repo.
- **One PR per issue.** Large PRs are hard to review (the reason for ADR-0009).

## Consequences

- PRs can be reviewed and merged one at a time, in any order, and reverted on their own.
- Nothing can land in a feature branch by mistake. Every merge reaches `develop`.
- Dependent work is slower: it waits for its groundwork to merge. Splits must be planned around
  independent slices, not around a chain.

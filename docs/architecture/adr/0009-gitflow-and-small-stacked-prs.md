# ADR-0009: GitFlow with small, stacked PRs and explicit issue tracking

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** #22, #42, [`datatrusthub-workflow` skill](../../../.claude/skills/datatrusthub-workflow/SKILL.md)

## Context

Large PRs (one frontend PR had about 1100 lines) are hard to review. Issues lagged behind merged work,
partly because PRs merge into `develop`, which is not the default branch, so `Closes #N` never closes anything.

## Decision

- **Branches:** feature and fix branches start from `develop`, and PRs target `develop`.
  `develop` is released to `main` (#22 reconciles the two).
- **PR size:** about 100–350 changed lines, excluding generated migration designer files. Larger work
  becomes a stack of PRs, each targeting the previous branch and building on its own.
  Docs and unrelated cleanups go in their own PRs.
- **Issues:** every piece of work has an issue, on the Project board, updated while the work happens.
  Issues are closed explicitly after the final PR merges.
- The full procedure is the `datatrusthub-workflow` Claude Code skill.

## Alternatives considered

- **One PR per issue regardless of size.** Slow, shallow reviews.
- **Rely on `Closes #N` keywords.** They do not work for PRs into `develop`.

## Consequences

- Faster, more careful reviews; each PR can be reverted on its own.
- Stacks need care: merge them in order, and retarget dependents to `develop` after a merge.
- Keeping issues and the board current is part of finishing a task, not optional extra work.

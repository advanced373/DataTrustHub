# ADR-0013: Three-agent development process: implementer, tester, reviewer

- **Status:** Accepted
- **Date:** 2026-10-21
- **Related:** #81, #82; [ADR-0009](0009-gitflow-and-small-stacked-prs.md)

## Context

Development is done with Claude Code agents. Until now, one agent wrote the code and the tests
for a task, and the user reviewed the PR. When the same agent writes the code and the tests, the
tests tend to confirm what the code does instead of what the spec requires. Reviewing their own
work, agents also missed issues that the user's review on #80 caught. The user asked for every
development task to involve three agents.

## Decision

Every code change (feature, bug fix, refactor) is carried out by three subagents with separate
responsibilities. The main session orchestrates them. The process is defined in
`.claude/skills/three-agent-dev/SKILL.md`, with the agents in `.claude/agents/`.

1. **Orchestrator** (main session):
   - writes a self-contained spec: numbered acceptance criteria, contract, scope, approvals;
   - prepares the worktree and runs the loop;
   - verifies the result, commits and opens the PR;
   - keeps the issues, the board and the ADRs up to date.

   It writes no production code and no tests.
2. **`feature-implementer`:** production code only, never tests.
3. **`feature-tester`:** tests derived from the acceptance criteria and the public contract, not
   from the implementation. It runs the full suite and classifies each failure as `IMPLEMENTATION`
   (back to the implementer) or `TEST` (fixed by the tester). It never edits production code.
4. **`code-reviewer`:** a read-only review, with no edit tools, against the spec, `CLAUDE.md`, the
   ADRs, a security checklist and the PR size. It runs the build and tests itself, and answers
   `APPROVE` or `CHANGES_REQUESTED` with findings for the implementer or the tester.

The agents work in one worktree, one after another, never in parallel. The loop repeats until
`APPROVE`, for at most 3 rounds, and is then escalated to the user. The PR records:
- the spec;
- the mapping from each criterion to its tests;
- the reviewer's verdict and how each finding was resolved.

Docs-only, ADR-only and catch-up PRs are exempt.

## Alternatives considered

- **One agent per task** (the previous process): fastest, but the agent marks its own work, as
  described in the context.
- **GitHub Copilot as the reviewer**, after the PR is opened, or in addition to the local reviewer.
  It would give a second model's opinion, but it does not run the build or the tests, gives no
  verdict (comments only), and needs our rules copied into `.github/copilot-instructions.md`.
  The user chose the local reviewer agent only.
- **Running the agents in parallel**, for example tester and implementer at the same time from the
  spec. Faster, but concurrent builds and edits in one worktree conflict, and separate worktrees
  would need merging.

## Consequences

- The tests are written independently of the code, and every change gets a reviewer that did not
  write it before the user sees the PR.
- Each task takes longer and uses more tokens: three agents and possibly several rounds.
- The spec becomes the central artifact: an unclear spec makes all three agents go wrong, so the
  orchestrator asks the user when a decision is missing.
- The reviewer's read-only status is enforced by its tools for files, and by its instructions for
  the shell, which it needs for `git diff`, the build and the tests.

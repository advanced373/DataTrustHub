---
name: three-agent-dev
description: Use for every DataTrustHub development task (feature, bug fix, refactor) - the main session orchestrates three subagents with separate responsibilities - feature-implementer writes the production code, feature-tester writes and runs the tests from the acceptance criteria, code-reviewer reviews read-only and approves or requests changes - and loops until the reviewer approves, then opens the PR. Not needed for docs-only or ADR-only changes.
---

# Three-agent development: implementer, tester, reviewer

Every code change goes through three agents. Each agent has one responsibility and does not do
the others' work. The main session is the **orchestrator**: it writes no production code and no
tests itself. This follows ADR-0013. Issues, small PRs and ADRs follow the `datatrusthub-workflow`
skill.

| Role | Agent (`.claude/agents/`) | Writes | Never touches |
|---|---|---|---|
| Orchestrator | main session | the spec, commits, PR, issue and board updates | production code, tests |
| Implementer | `feature-implementer` | production code (`DataTrustHub.Features`, `Domain`, `Infrastructure`, `API`, WebApp) | tests |
| Tester | `feature-tester` | tests in `DataTrustHub.Features.Tests` | production code |
| Reviewer | `code-reviewer` | nothing: it is read-only and reports findings | any file |

## Ubiquitous language

Everyone uses the vocabulary in [`docs/domain/ubiquitous-language.md`](../../../docs/domain/ubiquitous-language.md):
the orchestrator, every agent, the specs, reports, PR bodies, issues, code names and comments. It holds
the domain terms and the DDD / Clean Architecture building blocks (ADR-0012). Write specs in those
terms. When an agent reports a missing term or a mismatch, add the term to that page in the same PR.
A change to an entity, value object, invariant, domain error or bounded context also updates the
rest of the [domain handbook](../../../docs/domain/README.md) in the same PR.

## 1. Orchestrator: prepare

1. **Find or create the issue** (labels, board status *In progress*), as in the workflow skill.
2. **Write the spec.** The other agents work from it, so make it self-contained:
   - **Goal:** one or two sentences.
   - **Acceptance criteria:** numbered and testable. Example: "AC3: a non-owner gets 404 on `DELETE /data/{id}`".
   - **Contract:** routes, request and response shapes, status codes, error codes.
   - **Where:** the feature folders and slices to add or change, and the ADRs that apply.
   - **Out of scope.**
   - Written in the **ubiquitous language** (`docs/domain/ubiquitous-language.md`): the same terms the user and the code use.
   - **Approvals** the user gave: infrastructure, `_Shared`, the Database Owner role for migrations.
3. **Create the worktree:** `git worktree add ../wt-<slug> -b <branch> origin/develop`. All three agents
   work in this one worktree, **one after another, never in parallel**. Running them together would
   mean concurrent builds and edits in the same tree.
4. **Split large work** into small PRs first (workflow skill §1). Each PR runs the full three-agent loop.

## 2. Separate contexts

Each agent works in **its own context**, so that no agent is influenced by another agent's
reasoning. The tester must judge the code by the spec, not by the implementer's explanation of it;
the reviewer must form its own opinion of the diff, not adopt the others' conclusions.

- **A new spawn for every call.** Start each agent with the Agent tool and its own `subagent_type`,
  in every round. Never use `subagent_type: "fork"` (a fork inherits the orchestrator's whole
  context) and never continue an agent with `SendMessage`, neither across roles nor across rounds.
- **Facts cross between agents, reasoning does not.** The orchestrator passes on only what an agent
  can check for itself: the spec, the worktree, test names and their raw output, the reviewer's
  findings. It does not pass on an agent's explanations, self-assessments, suspected causes or
  "why I did it this way".
- **The orchestrator adds no opinion.** It does not hint at a solution beyond the spec, and does not
  tell an agent what it thinks of another agent's work.
- **Each agent reads the code itself.** The worktree is the shared source of truth; the reports are
  for the orchestrator and the PR body, not inputs to the other agents.

What each agent receives:

| Agent | Receives | Never receives |
|---|---|---|
| Implementer | spec, worktree; in a fix round: the failing tests with their acceptance criterion and raw output, or the reviewer's findings for code | the tester's suspected causes, the reviewer's reasoning beyond the finding itself |
| Tester | spec, worktree; in a re-run round: the list of files changed since its last run, or the reviewer's findings for tests | the implementer's report or explanations |
| Reviewer | spec, worktree, the tester's acceptance-criterion → test mapping and suite result; in a re-review round: its own previous findings and any dispute, stated as an argument | the implementer's summary or self-assessment, the tester's suspected causes |

## 3. The loop

Pass every agent the **full spec**, the **worktree path** and the round number. Agents start
cold, so everything they need (and only that, see §2) goes into the prompt.

```
round = 1
implementer(spec)                                  → implementation report
tester(spec)                                       → test report
while test report has IMPLEMENTATION failures and round ≤ 3:
    implementer(spec, failing tests + raw output)  → fix report
    tester(spec, files changed)                    → test report
reviewer(spec, worktree, AC → test mapping)        → APPROVE | CHANGES_REQUESTED
while CHANGES_REQUESTED and round ≤ 3:
    route each finding: code → implementer, tests → tester
    tester(re-run full suite)
    reviewer(previous findings + new diff)         → verdict
    round += 1
```

- **Test failures.** The tester classifies each failing test:
  - `IMPLEMENTATION` (the code does not meet an acceptance criterion): it goes back to the implementer;
  - `TEST` (the test itself is wrong): the tester fixes it.

  The implementer never edits tests to make them pass.
- **After 3 rounds without approval, stop** and ask the user. Show the open findings and what was
  tried. Do not loop further, and do not override the reviewer.
- **Disagreement.** If the implementer or tester disputes a finding, pass the argument (not who
  made it or how sure they were) to the reviewer in the next round. The reviewer decides, or escalates it to the user.
- **The spec is wrong or incomplete.** If an agent reports this, the orchestrator fixes the spec,
  asks the user if a decision is needed, and restarts the affected step.

## 4. Orchestrator: finish

1. Run the checks yourself in the worktree: `dotnet build DataTrustHub.sln` and
   `dotnet test DataTrustHub.Features.Tests/`. Smoke-test against LocalDB when the change affects SQL
   translation or the running app.
2. Commit with the attribution line from the conversation. Push and open the PR to `develop`.
3. **The PR body records the process:**
   - the spec and its acceptance criteria;
   - the implementer's summary;
   - the tester's mapping from each acceptance criterion to its tests, with the test results;
   - the reviewer's verdict, and every finding with how it was resolved.
4. Update the issue and the board. Write an ADR if a decision was made along the way.
5. Remove the worktree.

## Hand-off contracts

Every agent ends with a report in this shape, addressed to the orchestrator. The orchestrator uses
it for the loop and the PR body, does not re-derive it, and relays to the other agents only the
facts listed in §2.

- **Implementer:**
  - files changed, with one line each;
  - build result;
  - existing tests run and their result;
  - questions or spec gaps;
  - anything left undone.
- **Tester:**
  - tests added or changed, each mapped to its acceptance criterion (`AC2 → DeleteDataItemTests.NonOwner_Returns404`);
  - full-suite result (`passed/total`);
  - each failure with its classification (`IMPLEMENTATION` or `TEST`), output and suspected cause;
  - any criterion the tester could not test.
- **Reviewer:**
  - verdict: `APPROVE` or `CHANGES_REQUESTED`;
  - findings, ordered by severity, each as `[blocker|major|minor] file:line: problem → expected fix`, plus whether it is for the implementer or the tester;
  - what was checked.

## When not to use it

- Docs-only changes, ADRs, issue or board housekeeping, and catch-up PRs that carry already-reviewed
  commits: the orchestrator does these alone.
- If the user asks for a quick experiment that is not meant to be merged, ask first whether the
  three-agent process should apply.

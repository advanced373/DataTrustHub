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

Everyone uses the same words: the user, the orchestrator, every agent, the specs, reports,
PR bodies, issues, code names and comments. Domain terms are the DDD **ubiquitous language**.
Structure is described in **Clean Architecture / DDD** terms (ADR-0012). If something has no
term yet, or the code and the language disagree, report it rather than inventing a synonym.
New terms are added here.

### Domain terms

| Term | Meaning | In code |
|---|---|---|
| **User** | A person with an account. Role is `User` or `Admin` | `User` |
| **Organization** | The body users and policies belong to. Never *group*: the old *Group* names in the API and the UI are being renamed | `Organization` |
| **Policy** | A named access rule inside one organization. Classified data items belong to one policy | `Policy` |
| **Classification level** | A named sensitivity level with a priority, from Public (0) to Top Secret (4) | `ClassificationLevel` |
| **Clearance** | Grants a user one policy at one classification level. It allows every level at or below its own | `Clearance`, `Clearance.Allows` |
| **Data item** | A stored file (today a document) | `DataItem` |
| **Owner** | The user who uploaded a data item. Never use *uploader* in code | `DataItem.OwnerUserId` |
| **Personal data item** | A data item with no policy, readable by its owner only. Marking `UNCLASSIFIED` | `DataItem.CreatePersonal` |
| **Classified data item** | A data item with a policy and a classification level | `DataItem.CreateClassified` |
| **Security marking** | The label shown on a data item, e.g. `SECRET` | `DataItem.SecurityMarking` |
| **Read rule** | A user may read a data item if they own it, or hold a clearance on its policy at or above its level (ADR-0011) | `DataItem.IsReadableBy` |
| **Need-to-know** | Admin has no bypass of the read rule. An unreadable data item answers 404, so its existence is not revealed | — |

### Building blocks (DDD / Clean Architecture)

| Term | In this project |
|---|---|
| **Domain layer** | `DataTrustHub.Domain`: entities, value objects, domain errors, ports. No EF Core, no Infrastructure |
| **Application layer** | `DataTrustHub.Features`: one **use case** per vertical slice, as a command handler or query handler |
| **Infrastructure layer** | `DataTrustHub.Infrastructure`: adapters (repositories, mappers, `DContext`) |
| **Presentation layer** | `DataTrustHub.API` (Carter endpoints) and the WebApp |
| **Entity** | A domain object with identity and behaviour: `User`, `Organization`, `Policy`, `Clearance`, `DataItem` |
| **Aggregate root** | An entity loaded and saved as a whole through its own repository. Today each entity is its own aggregate root |
| **Value object** | Defined by its values, immutable: `ClassificationLevel` |
| **Invariant / business rule** | A rule that must always hold. It lives in entity methods (`IsReadableBy`, `MarkDeleted`, `CheckCanBeDeletedBy`), never in handlers or validators |
| **Factory method** | The only way to create a new entity, applying its rules: `User.Register`, `Policy.Create`, `Clearance.Grant`, `DataItem.CreateClassified` |
| **Rehydration** | Rebuilding a stored entity without its creation rules, through an `internal` constructor that only the persistence mappers see |
| **Domain error** | An expected rule failure returned as `Result`, not thrown: `UserErrors`, `DataItemErrors` |
| **Port** | An interface the domain or application needs, implemented outside it: `I*Repository`, `IUnitOfWork`, `IPasswordHasher` |
| **Adapter** | The Infrastructure implementation of a port |
| **Repository** | Port that loads and stores one aggregate as an entity. Commands only |
| **Unit of work** | Port that commits a command's changes in one transaction: `IUnitOfWork`, implemented by `DContext` |
| **Command / command handler** | A use case that changes state: load aggregates, call their behaviour, save once (`ICommandHandler`) |
| **Query / query handler** | A use case that only reads (`IQueryHandler`). It uses read models, not aggregates (CQRS read side, ADR-0012 step 4) |
| **Read model** | A flat shape built for display, served by an `I*Queries` port |
| **Persistence model** | The EF table shape `Db*` (`DbUser`, `DbDataItem`). Infrastructure only; never call it an entity |
| **Mapper** | Converts persistence model ⇄ entity (Mapperly), in Infrastructure |
| **Input validation** | Shape checks on a request (required, length, format) in the slice's `*Validator`. Not business rules |
| **Decorator** | A cross-cutting step wrapped around handlers by Scrutor: validation, later auditing |
| **Domain service** | A rule that needs several aggregates and belongs to none. None exist yet |

## 1. Orchestrator: prepare

1. **Find or create the issue** (labels, board status *In progress*), as in the workflow skill.
2. **Write the spec.** The other agents work from it, so make it self-contained:
   - **Goal:** one or two sentences.
   - **Acceptance criteria:** numbered and testable. Example: "AC3: a non-owner gets 404 on `DELETE /data/{id}`".
   - **Contract:** routes, request and response shapes, status codes, error codes.
   - **Where:** the feature folders and slices to add or change, and the ADRs that apply.
   - **Out of scope.**
   - Written in the **ubiquitous language** (section above): the same terms the user and the code use.
   - **Approvals** the user gave: infrastructure, `_Shared`, the Database Owner role for migrations.
3. **Create the worktree:** `git worktree add ../wt-<slug> -b <branch> origin/develop`. All three agents
   work in this one worktree, **one after another, never in parallel**. Running them together would
   mean concurrent builds and edits in the same tree.
4. **Split large work** into small PRs first (workflow skill §1). Each PR runs the full three-agent loop.

## 2. The loop

Start each agent with the Agent tool and its `subagent_type`. Pass the **full spec**, the
**worktree path** and the round number. Agents start cold, so everything they need goes into
the prompt.

```
round = 1
implementer(spec)                                  → implementation report
tester(spec, implementation report)                → test report
while test report has IMPLEMENTATION failures and round ≤ 3:
    implementer(spec, failing tests + output)      → fix report
    tester(re-run)                                 → test report
reviewer(spec, worktree, reports)                  → APPROVE | CHANGES_REQUESTED
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
- **Disagreement.** If the implementer or tester disputes a finding, pass the argument to the
  reviewer in the next round. The reviewer decides, or escalates it to the user.
- **The spec is wrong or incomplete.** If an agent reports this, the orchestrator fixes the spec,
  asks the user if a decision is needed, and restarts the affected step.

## 3. Orchestrator: finish

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

Every agent ends with a report in this shape. The orchestrator relays it, and does not re-derive it.

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

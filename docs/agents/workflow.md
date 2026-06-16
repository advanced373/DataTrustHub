# Multi-Agent Workflow

## Principle

One Claude Code agent = One GitHub issue = One git worktree = One feature folder.

Agents work independently. They share the same repository but each agent works on
a separate branch in a separate worktree. They do not communicate.

## Prerequisites

- Git 2.5+ (for worktrees)
- .NET 9 SDK
- SQL Server instance (for running the app; tests use InMemory)
- Read CLAUDE.md — it is the agent's primary context

## Launching an agent

### Step 1: Create a worktree for the task

```
git worktree add worktrees/agent-<feature-name> -b feat/<N>-<feature-name>
```

Example for issue 2 (Add data):
```
git worktree add worktrees/agent-data -b feat/2-add-data
```

### Step 2: Open Claude Code in that worktree

Open a new terminal and navigate to the worktree:
```
cd worktrees/agent-<feature-name>
claude
```

Claude Code automatically reads CLAUDE.md from the root. The agent now has full context.

### Step 3: Give the agent its task

Paste the GitHub issue title and description into the Claude Code session.
The agent reads CLAUDE.md, identifies its feature folder, and starts implementing.

### Step 3a: Agent marks the issue "In Progress"

Before writing any code, the agent updates the issue's status on the GitHub Project
board so the orchestrator can see at a glance which issues are actively being worked.

```
gh project item-list 2 --owner advanced373 --format json   # find the item id for the issue
gh project item-edit --project-id PVT_kwHOAyuapc4BFjHG --id <item-id> \
  --field-id PVTSSF_lAHOAyuapc4BFjHGzg22mlU --single-select-option-id 47fc9ee4
```

Requires `gh` authenticated with the `project` scope (`gh auth refresh -h github.com -s project`).
If the agent cannot reach GitHub (no `gh` auth available in its environment), it skips this
step rather than blocking — status hygiene is best-effort, not a gate on implementation.

### Step 4: Agent works in isolation

The agent creates files only inside Features/<FeatureName>/ and
Features.Tests/<FeatureName>/. It does not touch other folders.

### Step 4b: Agent pushes its branch, opens a PR, and marks the issue "In Review"

Once tests pass locally, the agent pushes its branch and opens a PR targeting `develop`
(not `main` — this repo follows GitFlow, where `main` is reserved for hotfixes), then
updates the issue's status on the GitHub Project board to reflect that it's now waiting
on review rather than still being written.

```
git push -u origin feat/<N>-<feature-name>
gh pr create --base develop --head feat/<N>-<feature-name> --title "..." --body "..."

gh project item-edit --project-id PVT_kwHOAyuapc4BFjHG --id <item-id> \
  --field-id PVTSSF_lAHOAyuapc4BFjHGzg22mlU --single-select-option-id df73e18b
```

Same best-effort caveat as Step 3a: if `gh` auth isn't available, skip the board update
rather than blocking on it — the PR itself is the thing that matters.

### Step 5: Review and merge

When the agent opens a PR:
1. Review the diff — check that isolation rules were followed
2. Run tests: dotnet test DataTrustHub.Features.Tests/
3. Merge to develop (not main — see Step 4b)
4. Move the issue to "Done" on the Project board (option id `98236657`)

### Step 6: Clean up the worktree

```
git worktree remove worktrees/agent-<feature-name>
```

## Database migrations

When agents need schema changes:
1. Agent writes // NEEDS MIGRATION next to the entity change
2. Orchestrator designates one agent as "Database Owner" for the sprint
3. Database Owner runs: dotnet ef migrations add <MigrationName> --project DataTrustHub.Infrastructure
4. Database Owner commits the migration files

## Running multiple agents in parallel

Sprint 1 — launch two agents simultaneously:
```
git worktree add worktrees/agent-auth -b feat/1-authentication
git worktree add worktrees/agent-mainview -b feat/16-main-view
```
Open two Claude Code terminals, one per worktree.

Sprint 2 — after Sprint 1 merges:
```
git worktree add worktrees/agent-add-data -b feat/2-add-data
git worktree add worktrees/agent-view-data -b feat/5-view-data
git worktree add worktrees/agent-admin -b feat/4-administration
```

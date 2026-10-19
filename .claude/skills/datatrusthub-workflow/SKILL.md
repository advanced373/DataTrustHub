---
name: datatrusthub-workflow
description: Use for any DataTrustHub work that touches git or GitHub: starting a task or issue, creating a branch, opening, updating or merging a PR, finishing a task, reporting project status, or finding a new bug or task along the way. Sets the required PR size and the GitHub issue lifecycle (issues must always be kept up to date and closed).
---

# DataTrustHub workflow: small PRs and issue lifecycle

Repo: `advanced373/DataTrustHub` (public). PRs target `develop`, not `main`.
If `gh` is not on PATH, use `"/c/Program Files/GitHub CLI/gh.exe"`.

## 1. Small PRs

- Keep each PR to roughly 100–350 changed lines, not counting generated EF migration `*.Designer.cs` files.
- Split larger work into a stack. Each PR targets the previous PR's branch, builds on its own and passes tests. Its body says `**Stacked on #N**` and the merge order.
- Plan the split before coding, and commit per slice.
- Never bundle docs or unrelated cleanups with a code change.
- Schema changes stack on the PR that holds the latest migration, so the EF snapshot stays linear.
- When a PR merges, retarget any PR stacked on it to `develop` (`gh pr edit N --base develop`).

## 2. Issue lifecycle: always update, always close

Every piece of work has an issue, and the issue always reflects reality, both on GitHub and on the Project board.

### Before starting
- Find the issue (`gh issue list`), or create it first. Give it labels (`backend`, `frontend`, `database`, `security`, `tech-debt`, `bug`, `enhancement`, `priority: high`), assignee `advanced373`, and milestone `MVP` when it belongs to the MVP.
- **Add every new issue to the Project board.** New issues do not appear there automatically.
- Move the issue to **In progress** on the board.

### While working
- Found a new bug or task? Create an issue right away, with labels, put it on the board as Backlog or Ready, and link it to the related issue. Don't just mention it in chat.
- **Security-sensitive findings** (leaked credentials, exploitable endpoints): the repo is public. Keep details off the tracker and tell the user instead. A neutral task issue ("Add authorization to X") is fine.
- The scope changes, the work is blocked, or a decision is made? Comment on the issue.
- Tick the task-list checkboxes in the issue body as items get done (`gh issue edit N --body ...`).

### When opening a PR
- In the PR body, use `Refs #N` for partial work and `Closes #N` for the PR that finishes the issue.
- Comment on the issue with the PR link if it is not obvious from the timeline.
- Move the issue to **In review** on the board.

### After a PR is merged
- `develop` is not the default branch, so **`Closes #N` does NOT auto-close the issue**. Close it explicitly:
  `gh issue close N --comment "Done in #PR"`
- Move it to **Done** on the board.
- Split work (`Refs #N`): comment on progress, and close the issue only when the last part is merged.
- Never leave an issue closed when its work isn't done. Reopen it, or create a follow-up issue and link it.

### Every session and every status report
- Before reporting status, sync: `gh pr list --state all --limit 20`. Close any issue whose final PR is merged but which is still open, update any issue whose state lags behind, and check the board matches.
- Pull status live from `gh`. Never keep a static status file in the repo.

## Project board commands

These need the `project` token scope. If it is missing, ask the user to run `! gh auth refresh -h github.com -s project`. Until then, skip board updates; never block on them.

```
# add an issue to the board (prints its item id)
gh project item-add 2 --owner advanced373 --url https://github.com/advanced373/DataTrustHub/issues/<N> --format json --jq .id
# find the item id of an existing issue
gh project item-list 2 --owner advanced373 --limit 100 --format json
# set the status
gh project item-edit --project-id PVT_kwHOAyuapc4BFjHG --id <item-id> \
  --field-id PVTSSF_lAHOAyuapc4BFjHGzg22mlU --single-select-option-id <status>
```

Status option ids: Backlog `f75ad846`, Ready `61e4505c` (next up), In progress `47fc9ee4`, In review `df73e18b`, Done `98236657`.

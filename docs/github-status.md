# GitHub Status — DataTrustHub

Snapshot of `advanced373/DataTrustHub` taken on 2026-10-19 from the GitHub API and local git history.

## Milestone

| Milestone | Due date | Open | Closed | Status |
|---|---|---|---|---|
| MVP | 2025-12-20 | 3 | 7 | Overdue |

## Open pull requests

### PR #20 — feat: implement DeleteDataItem slice (owner-only soft delete)

- Branch: `feat/7-delete-data` -> `develop`
- Size: 8 commits, 14 files, +753 / -56
- Mergeable: yes (clean, no conflicts)
- Review: Copilot left 3 comments, addressed in `e3b9ffa`
- Closes: #7
- Before merge:
  - [ ] Generate migration for `DbDataItem.IsDeleted` (`// NEEDS MIGRATION`)
  - [ ] Human review of the last commit `6283245` (touches legacy `DataTrustHub.API/Controllers/DataItemController.cs`)

No other PRs are open.

## Open issues

| Issue | Title | Milestone | Branch | Real status |
|---|---|---|---|---|
| #3 | Share data | MVP | — | Not started. Feature folder `Features/Sharing/` does not exist. Depends on #2 and #4. |
| #5 | View data | MVP | — | Not started. Needs `DataManagement/ViewDataItems` (`GET /data`, `GET /data/{id}`), filtered by user clearance. |
| #16 | Build basic structure for main view | MVP | `16-build-basic-structure-for-main-view` | PR #17 merged, but it delivered the VSA infrastructure, not the main view. `Features/MainView/GetDashboard` does not exist. |

## Closed issues that need attention

| Issue | Title | Problem |
|---|---|---|
| #4 | Administration of data | Closed, but only the Create slices exist (`CreateAccount`, `CreatePolicy`, `CreateGroup`). No update/delete/list and no clearance management. |
| #6 | Audit | Closed via PR #15, merged into `main` only. The audit pipeline is in the legacy `Application` layer and is missing from `develop` and from the VSA `Features/`. |
| #7 | Delete data | Closed on 2025-11-24 (legacy endpoints from #12), while the VSA implementation is still in open PR #20. |

## Branch state

- `origin/main` is 24 commits behind `origin/develop` and has 2 commits that `develop` lacks (audit, PR #15).
- Local `main` is 9 commits behind `origin/main` — run `git pull` on `main`.
- Worktrees `agent-add-data` and `agent-admin` are prunable (branches already merged).

## Task list

### High priority

- [ ] #21 Generate and apply EF migration for `IsDeleted`, then merge PR #20 (closes #7)
- [ ] Rotate the SQL password and move the connection string to User Secrets (no issue — kept off the public tracker)
- [ ] #22 Reconcile `main` and `develop`
- [ ] #27 Admin role missing from users and JWT — admin endpoints unreachable

### MVP features

- [ ] #5 View data — `DataManagement/ViewDataItems`
- [ ] #3 Share data — `Sharing/ShareData` (`POST /data/{id}/share`)
- [ ] #16 Main view — MVC WebApp frontend (PR open on `feat/16-main-view`)
- [ ] #23 Administration — remaining CRUD and `ManageClearances` (follow-up of #4)
- [ ] #24 Audit — port to the VSA Features project (follow-up of #6)

### Cleanup

- [ ] `git worktree prune`
- [ ] #25 Decide on the legacy `DataTrustHub.Application` / `API/Controllers` code
- [ ] #26 Move file content out of `DbDataItem.Content` into blob storage (optional)
- [ ] Update the MVP milestone due date

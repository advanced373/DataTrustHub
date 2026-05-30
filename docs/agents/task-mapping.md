# Task Mapping

Maps each GitHub issue to the feature folder and branch the responsible agent works in.

| GitHub Issue | Title | Feature Folder | Branch | Sprint |
|---|---|---|---|---|
| #1 | Autentification | Features/Authentication/ | feat/1-authentication | 1 |
| #16 | Build basic structure for main view | Features/MainView/ | feat/16-main-view | 1 |
| #2 | Add data | Features/DataManagement/ | feat/2-add-data | 2 |
| #5 | View data | Features/DataManagement/ | feat/5-view-data | 2 |
| #4 | Administration of data | Features/Administration/ | feat/4-administration | 2 |
| #3 | Share data | Features/Sharing/ | feat/3-share-data | 3 |

## Sprint ordering rationale

- Sprint 1 — Authentication and main view structure have no dependencies. Run in parallel.
- Sprint 2 — Add data, view data, and administration are independent of each other. Run in parallel. Depend only on users/organizations existing (from Sprint 1 Authentication).
- Sprint 3 — Share data depends on Add data (#2) and Administration (#4) being complete (policies and clearances must exist before sharing can be implemented).

## Use cases per feature folder

### Features/Authentication/
- RegisterUser/ — POST /auth/register
- LoginUser/ — POST /auth/login

### Features/DataManagement/
- AddDataItem/ — POST /data
- ViewDataItems/ — GET /data, GET /data/{id}

### Features/Sharing/
- ShareData/ — POST /data/{id}/share

### Features/Administration/
- ManageOrganizations/ — CRUD /organizations
- ManagePolicies/ — CRUD /policies
- ManageClearances/ — CRUD /clearances

### Features/MainView/
- GetDashboard/ — GET /dashboard

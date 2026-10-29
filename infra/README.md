# Infrastructure: production on Azure

DataTrustHub runs in one environment, `production`, in one Azure resource group (ADR-0021). The
resources were created by hand in the portal. This folder is the record of what exists, and holds
the scripts the owner runs once to finish the setup. There is no Bicep and no Key Vault
([ADR-0021](../docs/architecture/adr/0021-production-only-scripted-azure-setup.md)), and CD never
touches the infrastructure: the release workflow (#102) only applies migrations and deploys code.

**Changes made in the portal must be written back here by hand.** This page and the scripts are
the only record: the infrastructure is not reproducible from code.

## Resources

All in resource group `DataTrustHub`, region **Poland Central**. The names are the defaults of the
scripts' parameters.

| Resource | Name | Tier and key settings |
|---|---|---|
| App Service plan | `DataTrustHubPlan` | F1 (free), Linux |
| Web app, the API | `datatrusthubapi` | `DOTNETCORE\|10.0` (ADR-0020), HTTPS only, TLS 1.2 minimum, Always On off (not available on F1). After the setup: system-assigned managed identity, FTP disabled, basic publishing credentials (SCM and FTP) disabled, health check path `/health` |
| Web app, the WebApp | `DataTrustHub` | `DOTNETCORE\|10.0`, HTTPS only, TLS 1.2 minimum, Always On off, no managed identity (it holds no secret). After the setup: FTP disabled, basic publishing credentials disabled, health check path `/health` |
| Azure SQL server | `datatrusthub` | Microsoft Entra-only authentication (no SQL logins), the owner's account as Entra admin. After the setup: one firewall rule, `AllowAllWindowsAzureIps` (`0.0.0.0`–`0.0.0.0`), which lets Azure services in |
| Azure SQL database | `datatrusthub` | Serverless `GP_S_Gen5`, on the Azure SQL free offer, auto-pause when idle |
| Application Insights | `DataTrustHub` | Connected to both web apps through their app settings (below); its smart detection action group is created with it |

The web apps' host names have a random suffix. The scripts read them from Azure
(`az webapp show --query defaultHostName`); they are never written in the repository.

### App settings

The application secrets live in the API's App Service app settings, encrypted at rest by the
platform, never in the repository or in CI (ADR-0021 §4). Their values are not recorded here.

| Web app | Setting | Value | Set by |
|---|---|---|---|
| both | `APPLICATIONINSIGHTS_CONNECTION_STRING`, `ApplicationInsightsAgent_EXTENSION_VERSION`, `XDT_MicrosoftApplicationInsights_Mode` | Application Insights | the portal; kept by the setup script |
| API | `ConnectionStrings__DefaultConnection` | `Server=tcp:<server>.database.windows.net,1433;Database=<database>;Authentication=Active Directory Managed Identity;Encrypt=True;Connect Timeout=60`: the API reaches SQL with its managed identity, so no password exists | setup script |
| API | `Jwt__Secret` | random 48-byte key, base64 | setup script, generated only when missing |
| API | `AdminSeed__Email`, `AdminSeed__Password` | the first platform admin (ADR-0006) | setup script, asked from the owner when missing |
| WebApp | `Api__BaseUrl` | `https://<API default host name>` | setup script |

## Prerequisites

- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), signed in with `az login`,
  with the production subscription selected (`az account show`; change it with `az account set`).
- Owner or Contributor on the resource group `DataTrustHub`.
- For the SQL script: the Microsoft Entra admin of the SQL server.
- Windows PowerShell 5.1 or PowerShell 7.

## Order

1. **The setup script**, [`setup.ps1`](setup.ps1).
2. **The SQL script**, [`sql/grant-roles.sql`](sql/grant-roles.sql), for the API user.
3. **The deploy identity** of ADR-0018 §4, created by hand with its OIDC federated credential (#102).
4. **The SQL script again**, with the deploy identity filled in, for the deploy user.
5. **The first release** (#102).

### 1. The setup script

A PowerShell script, not bash: the owner works on Windows, and Windows PowerShell 5.1 is always
there. It reads the admin seed password as a secure string, which it never echoes. It uses the
Azure CLI only.

Run a dry run first. It prints every `az` command that would change Azure, with secrets masked,
and changes nothing. If the CLI is signed in, it reads the current state and prints only what
differs; otherwise it assumes nothing is set yet and uses placeholders.

```powershell
powershell -ExecutionPolicy Bypass -File infra\setup.ps1 -WhatIf
powershell -ExecutionPolicy Bypass -File infra\setup.ps1
```

The script shows the subscription and asks for confirmation (`-Force` skips it). Then it:

1. enables a system-assigned managed identity on the API only;
2. sets the API's app settings, keeping every other setting:
   - the connection string, with managed identity authentication;
   - `Jwt__Secret`, a new random key, **only if it does not exist yet**. Re-running does not
     rotate it, because rotating signs out every user. `-RotateJwtSecret` rotates it on purpose;
   - `AdminSeed__Email` and `AdminSeed__Password`, asked from the owner only if missing.
     The password is asked twice; an empty or malformed entry is asked again, up to three times.
     `-OverwriteAdminSeed` asks for them again and overwrites them;
3. sets the WebApp's `Api__BaseUrl` from the API's default host name;
4. disables FTP and basic publishing credentials (SCM and FTP) on both apps;
5. sets the health check path to `/health` on both apps;
6. adds the SQL firewall rule `AllowAllWindowsAzureIps` if it is missing;
7. prints what it changed and the next step, without any secret value.

It is idempotent: each step reads the current state first and changes only what differs, so a
second run reports nothing to change. If a read fails (for example a missing permission), the
script stops with an error naming the step instead of taking the failure for a missing setting,
so it never generates a new JWT signing key or asks for the admin seed by mistake. The settings
are passed to `az` in a temporary JSON file, deleted right after, so that no secret is on a
command line and `az.cmd` cannot mangle special characters in a password.

**Known gap (#150):** on the first real run the health check path was not applied to either web
app, although `az` reported no error; a second dry run still plans it.

Its parameters default to the resource names above: `-ResourceGroup`, `-ApiAppName`,
`-WebAppName`, `-SqlServerName`, `-DatabaseName`.

### 2. The SQL script, for the API user

Run [`sql/grant-roles.sql`](sql/grant-roles.sql) as the Entra admin of the SQL server, connected
to the database `datatrusthub` (not `master`), for example from the query editor of the database
in the portal. The variables at the top name the principals:
- `@ApiPrincipal`: the display name of the API's managed identity, which is the web app's name,
  `datatrusthubapi` by default;
- `@DeployPrincipal`: the display name of the deploy identity, `DataTrustHub-Deploy`. While it is
  empty or still the placeholder, the script grants the API user only and prints that the deploy
  user was skipped.

It creates the contained users `FROM EXTERNAL PROVIDER` and their roles (ADR-0018 §4):

| Identity | Database roles |
|---|---|
| the API's managed identity | `db_datareader`, `db_datawriter`: no DDL |
| the deploy identity | `db_ddladmin`, `db_datareader`, `db_datawriter`: applies migrations |

It is idempotent: every user and role membership is guarded with `IF NOT EXISTS`, so running it
again changes nothing that is already in place.

### 3. The deploy identity

Created by hand once (done on 2026-10-28). The release workflow signs in to Azure as this identity
through GitHub OIDC, with no stored secret.

1. **App registration** `DataTrustHub-Deploy` in Microsoft Entra ID, with its service principal.
   Do not give it the name of a web app: the SQL script finds principals by display name, and a
   web app's managed identity carries the web app's name.
2. **Federated credential**, issuer `https://token.actions.githubusercontent.com`, subject
   `repo:advanced373/DataTrustHub:environment:production`, audience `api://AzureADTokenExchange`.
   Only jobs running in the GitHub environment `production` can sign in as this identity.
3. **Azure role:** `Website Contributor` on each of the two web apps, scoped to the app, nothing
   else. It needs no right on the SQL server's firewall (ADR-0018, amendment of 2026-10-28): the
   rule `AllowAllWindowsAzureIps` already lets the GitHub-hosted runners in.
4. **GitHub environment `production`:** the owner as required reviewer, deployments from `main`
   only, and three environment **variables** (not secrets, the values are not secret):
   `AZURE_CLIENT_ID` (the app registration's application id), `AZURE_TENANT_ID`,
   `AZURE_SUBSCRIPTION_ID`. They are not written in this repository.

From Git Bash, the Azure CLI turns arguments that start with `/` into Windows paths: set
`MSYS_NO_PATHCONV=1` before `az role assignment create --scope /subscriptions/...`.

### 4. The SQL script again, for the deploy user

Run [`sql/grant-roles.sql`](sql/grant-roles.sql) again the same way, with `@DeployPrincipal` set
to `DataTrustHub-Deploy`. The API user is already in place and stays as it is; the script adds
the deploy user and its roles. The query editor runs only the selected text when there is a
selection: run the whole script, after editing the variable in the editor itself.

To check, the database should show five memberships: `DataTrustHub-Deploy` in `db_datareader`,
`db_datawriter`, `db_ddladmin`, and `datatrusthubapi` in `db_datareader`, `db_datawriter`.

### 5. The first release

The release workflow, [`.github/workflows/release.yml`](../.github/workflows/release.yml), applies
the migrations as the deploy identity and deploys the code (ADR-0018 §2, ADR-0021 §5). It needs
steps 1 to 4 done first; the first release applies every migration to the empty database.

**How a release runs.** Every push to `main`, which is the merge of a release (ADR-0009), starts
it; it can also be started by hand from the Actions tab (*Release*, *Run workflow*, branch
`main`). One release runs at a time, and a running release is never cancelled; of the runs
waiting for it, only the newest is kept.
1. **build**, without any Azure access: builds and tests the commit once, then uploads the API
   and WebApp packages, the migration bundle and the idempotent SQL script (`migrations-sql`).
   Nothing is rebuilt after this job.
2. **release**, in the GitHub environment `production`: it waits for the owner's approval, then
   signs in to Azure as `DataTrustHub-Deploy` through OIDC, applies the migrations, deploys the
   API, then the WebApp, and checks that both answer `/health`.

**Approving.** While the release job waits, the run's page in the Actions tab shows *Review
deployments*. Before approving, download the `migrations-sql` artifact of the same run to see what
the migrations will do to the production database. *Approve and deploy* starts the job; *Reject*
ends the run without touching Azure.

**When it fails.** The failed step's log says what went wrong; no secret is printed.
- **Apply migrations, the database is paused.** The free-offer database pauses when idle and
  takes up to about a minute to resume. The step retries for about three and a half minutes, and
  an attempt still running when that time is up is stopped; if the database was still resuming,
  re-run the job.
- **Apply migrations, firewall error** (a message such as *Client with IP address ... is not
  allowed to access the server*). The workflow opens no firewall rule: it relies on
  `AllowAllWindowsAzureIps` letting the GitHub-hosted runners in (ADR-0018, amendment of
  2026-10-28). Check that the rule still exists on the SQL server. If it does and the error
  stays, the rule-per-run of ADR-0018 §4 comes back, with a custom role limited to
  `Microsoft.Sql/servers/firewallRules/*` on the server: a change to the workflow and to the
  deploy identity, not something to work around by hand.
- **Apply migrations, login failed** (*Login failed for user '&lt;token-identified principal&gt;'*):
  the deploy user is missing in the database; run step 4.
- **Smoke check.** An F1 app can take minutes to cold start, and stops when the plan's daily CPU
  quota is used up. Look at the app's log stream in the portal, then re-run the job.

The migrations already applied stay recorded in the database, so a new attempt applies only what
is missing. A broken migration is fixed forward, in a new release (ADR-0018 §5).

**Re-running.** On the run's page, *Re-run jobs*, then *Re-run failed jobs*: the release job runs
again with the artifacts of the same run, after a new approval, so what reaches production is
still what that run built and tested.

## Deliberately manual

- Creating the resources: resource group, plan, web apps, SQL server, database, Application
  Insights. They are created once, in the portal (ADR-0021 §3).
- The deploy identity, its OIDC federated credential and the GitHub environment `production`
  with the owner as required reviewer (#102).
- No firewall rule for the release workflow: the GitHub-hosted runners reach the SQL server
  through `AllowAllWindowsAzureIps` (ADR-0018, amendment of 2026-10-28).

## Free-tier limits (ADR-0021 §6)

Free tiers are accepted for the MVP, with their consequences:
- **App Service F1:** 60 CPU minutes a day for the plan, shared by both apps; no Always On, so the
  apps sleep when idle and the first request after that is slow (cold start). When the CPU quota
  is used up, the apps stop until the next day. A single instance: the health check reports, but
  cannot move traffic to another instance.
- **Azure SQL free offer:** a monthly quota of vCore seconds and storage. The database pauses when
  idle, and the first connection after that waits for it to resume. When the quota is used up, the
  database pauses until the next month, and production stops with it.
- No SLA.

Because of them, the code must have a health endpoint that does not touch the database and SQL
connections that retry on transient errors (#146; until the endpoint exists, the health check
path answers 404), and CD wakes the database with retries before migrating
and retries its smoke check (#102). Moving to paid tiers (for example B1, and SQL billing over the
free limit) is due with the first real users.

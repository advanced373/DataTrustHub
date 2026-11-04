# ADR-0024: No App Service health check while the plan is on the Free tier

- **Status:** Accepted
- **Date:** 2026-11-02
- **Related:** #150, ADR-0021, #102

## Context

ADR-0021 runs production on the App Service F1 (Free) plan, which gives both web apps 60 CPU
minutes a day, shared, and no Always On. When the apps are idle they sleep and spend nothing.

The setup script (`infra/setup.ps1`) was meant to set the App Service health check path `/health`
on both apps. On the first real run `az` exited without an error but nothing changed. On
2026-11-02 both apps still had no health check path.

An App Service health check calls the path about once a minute on each instance. On F1 this keeps
both apps awake all day and spends the shared CPU quota. When the quota runs out, both apps stop
until the next day. With a single instance, the health check also has nowhere to move traffic, so
on this tier it would cost quota and give nothing back.

## Decision

- No App Service health check while the plan is on the Free tier. The setup script reads the plan's
  tier and skips the health check step on Free, printing that it skipped it.
- The CD smoke check (#102), which calls `/health` on both apps after each deploy, is the health
  signal for now.
- When the plan moves to a paid tier, the same script sets the health check path `/health`, with no
  code change.
- The setup script reads back every change it makes and fails if Azure does not hold the new value,
  so that a change Azure ignores is never reported as done.

## Alternatives considered

- **Fix the command and keep the health check on F1.** It keeps the apps awake, spends the shared
  CPU quota and cannot fail over with one instance.
- **Drop the step from the script.** We would then have to remember to set it by hand after
  moving to a paid tier.

## Consequences

- An app that stops responding between deploys is not detected by App Service. Users notice it
  first, or the next smoke check does.
- Moving to a paid tier (expected with the first real users, ADR-0021 §6): run the setup script
  again, and it adds the health check.
- A setup run fails loudly when Azure silently ignores a change, instead of reporting it as done.

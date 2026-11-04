# ADR-0027: Notification emails are sent directly, without a message broker

- **Status:** Accepted
- **Date:** 2026-11-04
- **Related:** #180, #172; [ADR-0026](0026-registration-requests.md), [ADR-0024](0024-no-app-service-health-check-on-the-free-tier.md), [ADR-0021](0021-production-only-scripted-azure-setup.md)

## Context

Since #172 a sign-up is a registration request that a platform admin approves or rejects
(ADR-0026). The applicant gets no signal: while the request is pending, login answers like an unknown
account. #180 adds an email on approve and on reject.

Something has to send the email, and the question came up whether a message broker (RabbitMQ was
named) should sit between the use case and the email service. The constraints:

- The volume is tiny: a few sign-ups a day at most, bounded by the per-IP limit and the cap of pending
  requests (ADR-0026).
- Production runs on the App Service **Free (F1)** plan. Its apps sleep when idle, so a background worker
  or a timer does not run reliably (the same reason ADR-0026 purges expired requests lazily, and
  ADR-0024 skips the health check).
- Everything else is an Azure managed service, set up by `infra/setup.ps1`, with the API authenticating
  through its managed identity and no extra secrets where avoidable.

A broker does not send email by itself: it moves messages between parts of the application. With or
without one, an email service does the sending.

## Decision

1. **Email service: Azure Communication Services (ACS) Email**, starting with an **Azure-managed
   domain** (a `DoNotReply@<id>.azurecomm.net` sender, no DNS work). A custom domain can replace it
   later without code changes, if mail lands in spam or the volume outgrows the managed domain's limits.
   The API sends with its managed identity (a role on the Communication Services resource), so no new
   secret is added.
2. **No message broker.** The approve and reject use cases call an email port directly, after their
   single save. A failed send never undoes the approval or rejection; it is logged (without the email
   address) and not retried. The applicant can always sign in once approved, so a lost email costs a
   delay, not access.
3. **If a broker is added later** (see "When to revisit"):
   - **Azure Queue Storage** when the need is only a durable work queue with one consumer (for example,
     retrying failed emails). It is the cheapest and simplest option.
   - **Azure Service Bus** when the need is a real broker: events with several consumers
     (topics and subscriptions, Standard tier), dead-lettering, duplicate detection or ordered
     sessions. It is the preferred broker for this project.
   - **Not RabbitMQ** (see below).

### Costs (as published by Microsoft at the time of writing; check the pricing pages before relying on them)

| Option | What is paid | Order of magnitude for this project |
|---|---|---|
| ACS Email, Azure-managed domain | per email (Microsoft's example rate: $0.00025) + per MB transferred ($0.00012) | cents a month |
| ACS Email, custom domain | the same + the domain registration | cents a month + about 10 € a year |
| Azure Queue Storage | per operation and stored GB, on a storage account | cents a month |
| Azure Service Bus Basic | per million operations, no base charge (queues only) | cents a month |
| Azure Service Bus Standard | a base charge per hour (first 13 M operations a month included); adds topics | about 10 € a month |
| RabbitMQ, self-hosted (container or VM) | compute, storage, backups, own patching | about 10–30 € a month + upkeep |
| RabbitMQ as a service (e.g. CloudAMQP) | a plan per month; the free plan is small and shared | 0 € (small free plan) to tens of € a month |

## Alternatives considered

- **RabbitMQ.** Rejected:
  - Azure offers no managed RabbitMQ, so it means hosting a container or VM (compute, storage,
    patching, monitoring, backups) or a third-party service (another vendor, another secret, data
    leaving Azure). Either costs more each month than the emails themselves.
  - It needs a consumer process. On the Free plan that worker sleeps with the app, so queued messages
    would wait until someone wakes the app, which removes the main benefit.
  - Its strengths (complex routing, very high throughput, AMQP interoperability with non-Azure systems)
    are not needs of this project.
  - It would be one more piece for `infra/setup.ps1`, the release pipeline and the tests.
- **Azure Service Bus now.** Not needed yet: one consumer, a handful of messages, and the same
  Free-plan worker problem. Its features (dead-lettering, duplicate detection, topics) pay off only
  with more consumers or volume.
- **Azure Queue Storage now.** The cheapest queue, but it still needs a worker that the Free plan does
  not keep awake, and nothing is lost today without it (a failed email does not block the applicant).
- **SendGrid or another email vendor.** Another vendor and a secret (an API key), where ACS uses the
  managed identity and stays inside the existing Azure subscription.
- **SMTP through a personal mailbox (Gmail, Outlook).** Tied to a personal account, needs an app
  password, and is not meant for application mail.

## Consequences

**Easier:**
- One small port and one adapter; no broker, no worker, no new secret.
- The monthly cost of notifications stays at cents.

**Harder:**
- A failed send is not retried; the applicant may get no email (they can still sign in once approved).
- The approve and reject requests wait for the email call. A timeout on that call must stay short so an
  outage of ACS does not slow the admin page down.
- The Azure-managed domain allows only a few emails per minute and about ten an hour (Microsoft Q&A
  figures; see the ACS service limits). A send over the limit is logged, not retried.

### When to revisit

Add **Queue Storage** if failed emails must be retried, or **Service Bus** if any of these happens:
- the same event needs more than one consumer (email plus something else), or messages must not be lost;
- the volume grows past the synchronous call (or past the managed domain, which a custom domain fixes
  first);
- the App Service plan moves off Free, so a background worker can run reliably (the same trigger as
  ADR-0024's health check).

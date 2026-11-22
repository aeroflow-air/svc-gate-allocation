# svc-gate-allocation

Assigns gates (stands) at LHR to departing flights and publishes the changes. Owner: platform squad. Standards: [ADR-0004](https://github.com/aeroflow-air/platform-handbook/blob/main/docs/decisions/0004-functional-style-csharp.md), [coding standards](https://github.com/aeroflow-air/platform-handbook/blob/main/docs/standards/coding-standards.md).

## Where things live
- `Domain/`: pure core. `GatePlanner.Apply(plan, command)` returns `Result<GatePlan>`. Value types (`GateId`,
  `FlightNumber`, `TimeWindow`) via `Parse`/`Create`, `Errors.cs`, `EventIds`, local `Result`/`Option` (BCL only).
- `Messaging/`: `FlightEventsProcessor` → `GateEventHandler` → planner; `OutboxDispatcher` → `IGateEventPublisher`.
- `Storage/`: `IGatePlanStore` (atomic `Update`), in memory. `Controllers/`: HTTP edge, errors via `ProblemResults`.

## Non-negotiables (* = enforced by build or CI in this repo)
- `[ApiController]` controllers, not minimal APIs. Errors leave as ProblemDetails 400/404/409 with `code` via
  `ProblemResults`. Write endpoints have no auth policy yet (ADR-0018; baggage-reclaim has the pattern).
- The domain is pure: static functions, `sealed record`s, `Result<T>` with typed errors, `Option<T>` not `null`.
  * `DomainPurityTests`: nothing in `Domain/` uses logging, I/O, HTTP, data access, Microsoft.Extensions/ASP.NET,
  ServiceDefaults, other layers, `TimeProvider` or `DateTime(Offset).Now`; pass `now` in from the edge.
  Review-only: `throw` (only `UnreachableException`), `null`, `Guid.NewGuid`/`Random`.
- * `AddServiceDefaults()` + `MapDefaultEndpoints()`: OTel, request and messaging logs, `/health`, `/alive`
  (`HealthEndpointTests`, `ObservabilityContractTests`). Don't write your own HTTP or messaging log lines.
- * `[LoggerMessage]` only (CA1848/CA2254 are build errors). Never `Console`/`Debug.WriteLine` (RS0030, from
  `AeroFlow.ServiceDefaults` 0.1.1). Business event ids start at 10,000. No PII, secrets or bodies in logs.
- Options use `Configure<T>` with no startup validation yet; new options use `ValidateOnStart()`. Workflows pin
  `aeroflow-workflows` tags (`dotnet-ci.yml@v0.7.0` with `github-packages: true` and `packages: read`; `v0.7.0`
  for skills), never `@main`, with job `permissions`.

## Events (ADR-0013/0017/0019)
- Consumes `flight-events` (the simulator's, in aeroflow-local; never publish to it). Publishes only `gate-events`,
  via the outbox: `GateEventHandler` saves the plan change and its events in one `IGatePlanStore.Update`; `OutboxDispatcher` sends them (MessageId = event id, Subject = type, CorrelationId = flight id).
  Never call the publisher from a handler. Examples in `contracts/gate-events/`; changes are additive.
- Event ids are deterministic, `EventIds.Derive(triggeringEventId, eventType)`, never `Guid.NewGuid`. Handlers are
  idempotent: the processed id is stored in the same atomic update, so a redelivery is `Ignored`. Late or
  out-of-order events return `Ignored`/`Rejected`, never throw. An unknown `FlightEventType` is dead-lettered as
  `unreadable`, so a new flight event type needs this service updated first.
- Handler, outbox and contract tests cover this, but nothing blocks a direct publish. * `workload.yaml` is
  schema-checked (`workload.yml`); it doesn't declare `queue`/`events:` yet, which is a separate fix.

## Build, test, docs, skills
`dotnet build -c Release && dotnet test`; domain tests need no mocks. Update `docs/service.md` with behaviour changes.
Plain Markdown in `.claude/skills/<name>/SKILL.md`, pinned in `.agent-skills.yaml`; read each `description`, follow the
Steps. Don't edit `aeroflow-*` folders (* CI drift check; CODEOWNERS). Change them in [agent-skills](https://github.com/aeroflow-air/agent-skills).

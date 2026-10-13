# svc-gate-allocation

Assigns gates (stands) at LHR to departing flights and publishes the changes.

## Conventions (read before changing code)

This repo follows [ADR-0004: functional-style C#](https://github.com/aeroflow-air/platform-handbook/blob/main/docs/decisions/0004-functional-style-csharp.md).
The checklist is [C# coding standards](https://github.com/aeroflow-air/platform-handbook/blob/main/docs/standards/coding-standards.md). In short:

- Domain logic is **pure static functions** composed with `Bind` / `Map` / `Match` (like `GatePlanner.Apply(plan, command)`).
- Domain types are **immutable `sealed record`s** with immutable collections; change means returning a new value.
- States are **closed sum types**, ids are **value types** built via `Parse` / `Create`, never raw strings.
- Business failures return **`Result<T>` with typed error records** from `Domain/Errors.cs`
  (`ValidationError` / `NotFoundError` / `RuleViolation` → 400 / 404 / 409). No exceptions for business rules or control flow;
  `throw` only for bugs (`UnreachableException`) or invalid startup data.
- Absence is **`Option<T>`**, not `null`. Nullable types only in HTTP request/response records and wire messages.
- **No side effects in the domain**: no I/O, logging, `Console`, messaging, storage, static mutable state or
  `DateTimeOffset.UtcNow`. Time comes from `TimeProvider` at the edge.
- BCL only: the local `Result` / `Option` are deliberately small. No LanguageExt.
- Edges: async all the way (no `.Result` / `.Wait()`), flow `CancellationToken`, `[LoggerMessage]` structured logging.

## Where things live

- `src/AeroFlow.GateAllocation/Domain`: pure core. `GatePlanner.Apply(plan, command)` returns `Result<GatePlan>`; `GatePlan`, `Gate`, `Turn`, value types (`GateId`, `FlightNumber`, `TimeWindow`), `Errors.cs`, `Result`, `Option`.
- `Messaging/`: `FlightEventsProcessor` (Service Bus) → `GateEventHandler` → planner; `ServiceBusGateEventPublisher`.
- `Storage/`: `IGatePlanStore` (atomic `Update`), in-memory implementation.
- `Controllers/`: HTTP edge, maps errors to problem details.

## Events

- Subscribes to `flight-events` (owned by the flight simulator in aeroflow-local). Never publish to it.
- Publishes only to its own topic, **`gate-events`**, through `IGateEventPublisher` (MessageId = event id, Subject = event type). CorrelationId is the flight id when there is one and falls back to the flight number; new events should always carry the flight id.
- Handlers must be **idempotent**: Service Bus can redeliver, so applying the same event twice must not change the plan twice. Here a repeat allocation is rejected by the planner (`AlreadyAllocated`) and a repeat release finds no allocation; keep new commands the same.
- Late or out-of-order events of a known type return an `Ignored` / `Rejected` outcome with a reason; don't throw for them. An **unknown `FlightEventType`** value is different: it fails JSON deserialisation and `FlightEventsProcessor` dead-letters it as `unreadable`. Adding an event type to `flight-events` therefore needs this service updated first.
- **Known gap, don't copy it:** publishing is at most once. The plan is saved before the gate events are sent; if the send throws, the redelivered message is rejected as a repeat and the gate event is lost. Gate event ids are random (`Guid.NewGuid`), so consumers can't dedupe a re-publish either. The fix (outbox or deterministic ids) is tracked in [platform-handbook#84](https://github.com/aeroflow-air/platform-handbook/issues/84); new publishing code should follow that, not this pattern.

## Tests and docs

- New domain functions and handlers need unit tests in `tests/`, including failure and duplicate-delivery paths.
  Domain tests need no mocks.
- Keep `docs/service.md` current: update it in the same PR when inputs, outputs, settings or known failure
  modes (with log signatures) change.

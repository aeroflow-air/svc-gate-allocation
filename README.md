# AeroFlow Gate Allocation

Thin ASP.NET Core Web API that puts flights on gates in the [AeroFlow Air](https://github.com/aeroflow-air) portfolio. Health checks, structured logging, ProblemDetails, and a stub for OpenTelemetry — without a shared framework package.

## Local run

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet restore
dotnet run --project src/AeroFlow.GateAllocation
```

- API: `http://localhost:8080` (or the port shown in the console)
- Health: `GET /health`
- Gate probe: `GET /api/gates/ping`
- Gates and allocations: see [Gate allocation](#gate-allocation)

```bash
dotnet test
```

## Gate allocation

The domain lives in [`src/AeroFlow.GateAllocation/Domain`](src/AeroFlow.GateAllocation/Domain) and is pure: no clock, storage or HTTP.

- **Value types** (`FlightNumber`, `GateId`, `TimeWindow`) can only be built through `Parse` / `Create`, which return a `Result`. A `TimeWindow` is the on-block to off-block interval, and its start is always before its end.
- **`AircraftSize`** is the ICAO aerodrome code `C` to `F`, ordered smallest to largest.
- **Product types**: a `Gate` is an id, the largest size it takes, and a status. A `Turn` is a flight, its aircraft size and a window. An `Allocation` is a `Turn` on a `GateId`. A `GatePlan` holds the gates, the allocations and the minimum turnaround.
- **Sum types**: `GateStatus` is `Open` or `Closed(Reason)`. `GateCommand` is a closed set: `AllocateGate`, `ReassignGate`, `RetimeTurn`, `ReleaseGate`, `CloseGate`, `ReopenGate`. `Result<T>` is `Ok` or `Failure`, and `Option<T>` is `Some` or `None`.
- **`Option<T>` instead of `null`**: lookups (`GatePlan.FindGate`, `GatePlan.AllocationFor`, `GatePlanner.Suggest`) return `Option`. `AllocateGate.Gate` is an `Option<GateId>`, where `None` means "pick one for me". `ToResult` turns `None` into a typed error. Nullable types appear only in the HTTP request and response records.
- **`GatePlanner.Apply(plan, command)`** returns a `Result<GatePlan>`: either a new plan or a typed error with a stable code.
- **Rules**:
  - A flight holds at most one allocation.
  - The aircraft must be no larger than the gate's size.
  - The gate must be open.
  - Two flights on the same gate must be at least the minimum turnaround apart (15 minutes in the demo). Windows exactly that far apart are allowed.
  - Reassign and retime follow the same rules. A flight's own current slot never conflicts with itself.
  - A gate cannot be closed while it still has allocations.
- **`GatePlanner.Suggest`** picks the smallest open gate that fits and is free, then the lowest gate id. This keeps big gates free for big aircraft. Allocating with no gate uses it, and fails with `no_gate_available` when it returns `None`.

Demo gates `7`, `9`, `12`, `14`, `21`, `55A` and `R1` are seeded in memory at start-up. `R1` is closed. The allocations use times relative to the current time, and each is made by applying real commands. The flight numbers that depart from LGW match the flight-status demo. State resets when the process restarts.

### Endpoints

| Method | Route | Responses |
| --- | --- | --- |
| `GET` | `/api/gates/ping` | 200 |
| `GET` | `/api/gates` | 200 |
| `GET` | `/api/gates/{gate}` | 200, 400, 404 |
| `POST` | `/api/gates/{gate}/close` | 200, 400, 404, 409 |
| `POST` | `/api/gates/{gate}/reopen` | 200, 400, 404, 409 |
| `GET` | `/api/allocations/{flightNumber}` | 200, 400, 404 |
| `POST` | `/api/allocations` | 201, 400, 404 (unknown gate), 409 (rule broken) |
| `PUT` | `/api/allocations/{flightNumber}/gate` | 200, 400, 404, 409 |
| `PUT` | `/api/allocations/{flightNumber}/window` | 200, 400, 404, 409 |
| `DELETE` | `/api/allocations/{flightNumber}` | 204, 400, 404 |

Errors are `application/problem+json`, and a `code` extension carries the error code (for example `gate_conflict`, `aircraft_too_large`, `gate_closed`, `no_gate_available`).

```bash
curl http://localhost:8080/api/gates
curl http://localhost:8080/api/allocations/AF204

# Let the planner choose: a code E aircraft lands on gate 14 (or 55A if 14 is busy)
curl -X POST http://localhost:8080/api/allocations \
  -H 'Content-Type: application/json' \
  -d '{"flightNumber":"AF100","aircraftSize":"E","onBlock":"2026-09-25T18:00:00Z","offBlock":"2026-09-25T19:00:00Z"}'

# 409 with "code": "gate_has_allocations"
curl -X POST http://localhost:8080/api/gates/12/close \
  -H 'Content-Type: application/json' \
  -d '{"reason":"Jet bridge fault"}'
```

This service follows [ADR-0004: functional-style C#](https://github.com/aeroflow-air/platform-handbook/blob/main/docs/decisions/0004-functional-style-csharp.md). It uses immutable records, closed sum types, and a pure domain core with side effects at the edges (controllers, store and `TimeProvider`). Failures come back as explicit `Result` values rather than exceptions, and absent values as `Option` rather than `null`. It uses only the BCL, with no FP library.

## Container

```bash
docker build -t aeroflow-gate-allocation .
docker run --rm -p 8080:8080 aeroflow-gate-allocation
```

Then `curl http://localhost:8080/health`.

## What is deliberately not included

- **No Kubernetes** manifests or Helm charts
- **No Dagger** pipelines
- **No Pulumi** (or other IaC frameworks in-repo yet)
- **No heavy shared framework** NuGet — composition stays in `Program.cs` so squads can delete or replace pieces freely
- **No link to flight-status yet**: delays there do not retime turns here automatically. `PUT .../window` is the hook for that.

Infrastructure as Bicep/AVM will land under [`infra/`](infra/README.md) later. Platform conventions live in the **platform-handbook**; reusable Actions come from **aeroflow-workflows**.

## CI

This repository calls the reusable workflow in `aeroflow-workflows` (pinned to `@v0.1.0`) via `.github/workflows/ci.yml`, targeting `AeroFlow.GateAllocation.sln`.

## Pointers

| Resource | Purpose |
| --- | --- |
| platform-handbook | Portfolio standards, CLAUDE.md constraints, ADR process |
| aeroflow-workflows | Shared GitHub Actions (dotnet-ci and decisions validation) |
| `infra/` | Placeholder for Bicep/AVM — see `infra/README.md` |

## Licence / ownership

Internal AeroFlow Air service. Public repository under `aeroflow-air`.

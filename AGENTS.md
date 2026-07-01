# Project Summary

Inventory Reservation System is a two-service .NET application with OrderService and InventoryService.
It supports atomic batch reservations for multiple SKUs using all-or-nothing semantics.
Reservations expire after 10 minutes unless confirmed, then inventory is automatically released.
The system uses REST, gRPC, MongoDB, Redis, Docker Compose, and tests for consistency, concurrency, and resilience.

# Current Repository Structure

```text
InventoryReservationSystem/
├── Docs/
│   └── about-project/
│       └── project-definition-and-requirements.md
├── src/
│   ├── contracts/
│   │   └── protos/
│   └── services/
│       ├── OrderService/
│       │   ├── OrderService.API/
│       │   │   ├── Contracts/
│       │   │   ├── Endpoints/
│       │   │   ├── Properties/
│       │   │   └── Program.cs
│       │   ├── OrderService.Application/
│       │   │   └── Orders/
│       │   │       ├── Abstractions/
│       │   │       ├── Commands/
│       │   │       └── Queries/
│       │   ├── OrderService.Domain/
│       │   │   └── Orders/
│       │   └── OrderService.Infrastructure/
│       │       ├── InventoryGrpc/
│       │       └── Mongo/
│       └── InventoryService/
│           ├── InventoryService.API/
│           │   ├── Grpc/
│           │   ├── Properties/
│           │   └── Program.cs
│           ├── InventoryService.Application/
│           │   └── Reservations/
│           │       ├── Abstractions/
│           │       └── Commands/
│           ├── InventoryService.Domain/
│           │   ├── Inventory/
│           │   └── Reservations/
│           └── InventoryService.Infrastructure/
│               ├── BackgroundJobs/
│               ├── Mongo/
│               └── Redis/
├── test/
├── InventoryReservationSystem.AppHost/
├── InventoryReservationSystem.ServiceDefaults/
├── InventoryReservationSystem.slnx
└── README.md
```

# Architecture

Services follow Clean Architecture. `Domain` contains core entities and business rules, `Application` contains use cases and abstractions, `Infrastructure` contains MongoDB, Redis, and gRPC implementations, and `API` contains REST/gRPC endpoints plus composition root setup.
Dependencies must point inward: `API` and `Infrastructure` depend on `Application`/`Domain`, `Application` depends on `Domain`, and `Domain` depends on nothing.

# Service Communication

- `OrderService` is the public entry point and exposes REST endpoints for creating, reading, confirming, and cancelling orders.
- `OrderService` must call `InventoryService` through gRPC for inventory operations; do not access inventory data directly from `OrderService`.
- `InventoryService` owns `ReserveBatch`, `ReleaseBatch`, `ConfirmReservation`, and `GetStock` style operations, using deterministic Redis locks for all SKU batches.
- Order creation flow: REST request enters `OrderService`, idempotency is checked in Redis, order is stored as pending, `ReserveBatch` is called over gRPC, then result is persisted atomically from the order perspective.
- Cancel/expire flow: `OrderService` marks order cancelled/expired and calls `InventoryService` over gRPC to release the reservation; release must be idempotent.
- Both services should propagate correlation IDs and OpenTelemetry trace context across REST and gRPC boundaries.
- gRPC calls from `OrderService` should use Polly retry, timeout, and circuit breaker policies for graceful degradation when `InventoryService` is slow or unavailable.

# Requirements Reference

Detailed project definition, requirements, evaluation scenarios, and deliverables are documented in `Docs/about-project/project-definition-and-requirements.md`.

# Engineering Approach

This repository favors incremental, reviewed changes over large autonomous implementations.

- Explain the approach and trade-offs before implementing.
- Do not implement a full feature end-to-end without an explicit go-ahead.
- Work in small, testable units; avoid opportunistic multi-file changes.
- When introducing a new protocol or pattern (e.g. a new gRPC contract, a new
  concurrency strategy), state the concept explicitly rather than assuming it's known.
- When proposing code, contrast a naive approach with the correct one and explain why
  the naive one fails (concurrency, transaction boundaries, performance, etc.).

# Workflow

1. Understand the requirement.
2. Identify constraints and hidden dependencies.
3. Explain approach before writing code (see Engineering Approach).
4. Execute in small, testable units.
5. After finishing a step, state the next recommended task explicitly.

# Maintenance

Update this file when a service boundary, communication pattern, or structural
convention changes. Remove stale or incorrect notes on sight — do not let this
file become a graveyard of outdated decisions.

# Current Focus

Implementing: gRPC contract definitions (`src/contracts/protos`).
Next: OrderService REST skeleton (`Endpoints`), no business logic yet.

# Notes

- `OrderService` owns REST order endpoints and calls `InventoryService` over gRPC.
- `InventoryService` owns stock, reservations, Redis locking, expiry, and release/confirm flows.
- Domain projects contain core entities and statuses; Application projects contain use cases
  and abstractions; Infrastructure projects contain MongoDB, Redis, and gRPC integrations.
- `test/`, scripts, Docker Compose, and proto files are not fully created yet; add them when
  implementation starts.
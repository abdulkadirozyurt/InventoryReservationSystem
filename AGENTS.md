# InventoryReservationSystem — Project Blueprint & Agent Rules

**Operating mode:** Caveman mode: short, clear, explain why before what, preserve strict architectural intent.

`AGENTS.md` is the living source of truth for project status, roadmap, phase checklists, service boundaries, communication patterns, and structural conventions. Update it when these change; remove stale or incorrect notes immediately.

`README.md` is the polished public showcase, not an implementation dump. Keep it accurate after public-facing architecture, dependency, setup, endpoint, workflow, or status changes. Put per-file/per-operation details in plans, `AGENTS.md`, or focused docs. Add/update helpful visuals—especially Mermaid service-flow or proto-structure diagrams—when they improve public understanding.

## 1. Agent Operating Contract

### Roles and delegation

- **Main Agent (Expert Software Architect):** Think, scan, define the Definition of Done (DoD), decompose, delegate, review, integrate, and update status. Do not implement non-trivial tasks directly.
- **Sub-agents:** Isolated specialist worker contexts that inspect files, change code, run CLI commands, and execute tests.
- **Mandatory delegation:** Every task must use at least one sub-agent for execution or inspection.
- Before delegation, the Main Agent must understand the business constraints (`.NET`, MongoDB replica set, Redis, gRPC), identify hidden dependencies (for example, a `.proto` change affecting both services), define an explicit DoD, and split work into independent testable units.
- Every delegation must state an exact scope and the files the sub-agent may touch. The Main Agent must review all output before accepting or merging it and must keep phase checklists current.

### Task classification

A task is **trivial** only when it is one deterministic change with no architectural or dependency risk.

- **Trivial examples:** Fix one log-message typo; change one `appsettings.json` key; run one read-only query or obvious test command.
- **Non-trivial examples:** Add a gRPC endpoint; configure Health Checks; set up Docker Compose replicas; modify Domain entities. These must never be implemented directly by the Main Agent.

### Concurrency

- Changes to shared entry points—`Program.cs`, `ServiceDefaults`, `.slnx`, and `docker-compose.yml`—must be delegated **sequentially**, never in parallel.
- Parallel sub-agents are allowed only for truly independent work with no shared files, such as separate `OrderService.Domain` and `InventoryService.Domain` changes.

### Required workflow

1. **Analyze:** Read the request and current checklist state.
2. **Scan:** Inspect relevant structure and hidden blockers.
3. **Plan:** Write a short bounded execution plan and DoD.
4. **Delegate:** Assign precise scope and allowed files to a sub-agent.
5. **Verify:** Review architecture, build, tests, and runtime behavior.
6. **Integrate:** Accept clean changes and update the relevant `[ ]` to `[x]` item in **Current Focus**.
7. **Report:** Briefly state the exact result and explicitly name the **Next Recommended Task**.

### Verification gate

Reject work immediately if any rule below is violated:

1. **Inward dependencies:** `Domain` has zero dependencies; `Application` depends only on `Domain`; `Infrastructure` and `API` depend on `Application`.
2. **Service isolation:** `OrderService` never accesses MongoDB collections or Redis instances owned by `InventoryService`; inventory interaction is gRPC-only.
3. **Tracking:** Every network/gRPC call propagates `CorrelationId` and OpenTelemetry trace context.
4. **Resilience:** Model retry, timeout, circuit breaker, and rate limiting with an appropriate Polly pipeline:
   - retry/timeout: transient dependency failures and controlled waits;
   - circuit breaker: genuine external dependency failures;
   - rate limit: ingress/egress traffic control;
   - normal business contention, including Redis lock contention, must not open a circuit breaker.
5. **Build and tests:** The code must compile and relevant tests must pass.
6. **Immediate Compose proof:** Before starting the next feature or completing its checklist item, Docker-build and verify the feature in the running Compose environment.
7. **Runtime proof:** Run the smallest end-to-end request through the changed path; inspect relevant container logs and, when writes or locks are involved, MongoDB/Redis state.

## 2. Project Blueprint

### Summary

A two-service .NET system (`OrderService`, `InventoryService`) for atomic, all-or-nothing batch reservations across multiple SKUs. Unconfirmed reservations expire after 10 minutes and inventory is automatically released. The stack uses REST, gRPC, MongoDB, Redis, Docker Compose, and consistency/concurrency/resilience tests.

### Repository structure

```text
InventoryReservationSystem/
├── Docs/about-project/requirements.md
├── src/
│   ├── contracts/protos/
│   └── services/
│       ├── OrderService/
│       │   ├── OrderService.API/{Contracts,Endpoints,Properties,Program.cs}
│       │   ├── OrderService.Application/Orders/{Abstractions,Commands,Queries}
│       │   ├── OrderService.Domain/Orders/
│       │   └── OrderService.Infrastructure/{InventoryGrpc,Mongo}
│       └── InventoryService/
│           ├── InventoryService.API/{Grpc,Properties,Program.cs}
│           ├── InventoryService.Application/Reservations/{Abstractions,Commands}
│           ├── InventoryService.Domain/{Inventory,Reservations}
│           └── InventoryService.Infrastructure/{BackgroundJobs,Mongo,Redis}
├── test/
├── InventoryReservationSystem.AppHost/
├── InventoryReservationSystem.ServiceDefaults/
├── InventoryReservationSystem.slnx
└── README.md
```

### Ownership, communication, and flows

- `Domain` projects contain core entities and statuses; `Application` projects contain use cases and abstractions; `Infrastructure` projects contain MongoDB, Redis, and gRPC integrations.
- `OrderService` is the public entry point and owns REST endpoints for create, read, confirm, and cancel operations.
- `InventoryService` owns stock, reservations, Redis locking, expiry, release/confirm flows, and `ReserveBatch`, `ReleaseBatch`, `ConfirmReservation`, and `GetStock`-style operations. All SKU batches use deterministic Redis locks.
- **Create order:** REST enters `OrderService` → check idempotency in Redis → store pending order → call `ReserveBatch` by gRPC → persist the result atomically from the order perspective.
- **Cancel/expire:** `OrderService` marks the order cancelled/expired → calls `InventoryService` by gRPC → release is idempotent.
- Both services propagate correlation IDs and OpenTelemetry trace context across REST/gRPC boundaries.
- `OrderService` gRPC calls use Polly retry, timeout, and circuit breaker policies for graceful degradation when `InventoryService` is slow or unavailable.

### Engineering approach

- Prefer incremental, reviewed changes over large autonomous implementations.
- Explain approach and trade-offs before implementation.
- Do not implement a complete feature end-to-end without explicit user approval.
- Work in small testable units; avoid opportunistic multi-file changes.
- Explain any new protocol or pattern, such as a gRPC contract or concurrency strategy.
- When proposing code, contrast the naive and correct approaches and explain why the naive one fails (for example, concurrency, transaction boundaries, or performance).

### Logging and audit trail

- All technical application logs use structured Serilog logging.
- Use message templates and named properties; never string interpolation or manual concatenation.
  - Wrong: `_logger.LogInformation($"Order {orderId} processed.");`
  - Correct: `_logger.LogInformation("Order {OrderId} processed.", orderId);`
- Runtime logs should carry searchable named properties where applicable: `CorrelationId`, `OrderId`, `ReservationId`, `Sku`, `WarehouseId`, `LockKey`, `ElapsedMs`, and error category.
- Audit records such as `InventoryTransactions` and `OrderHistory` are durable domain evidence, not technical logs; write them with the related runtime flow.

### Requirements reference

Detailed definition, requirements, evaluation scenarios, and deliverables: [requirements.md](/Docs/about-project/raw-requirements.md).

## 3. Current Focus & Roadmap

### FAZ 6: İzlenebilirlik Panelleri, Doğrulama, Stres Testleri ve Kararlılık Kontrolleri

**Hedef:** Sistemin yüksek yük altında kilitlenmediğini, veri kaybetmediğini ve operasyonel olarak izlenebilir olduğunu kesin olarak kanıtlamak.

- [ ] **6.1 — Envanter Tutarlılık Metriklerinin İzlenmesi**
  - Grafana: `time-to-reserve`, `time-to-expiry`, `time-to-confirmation`, database slow-query tracking, connection-pool usage, and per-service memory/CPU.
  - MongoDB: threshold-based slow-query log analysis in a dedicated panel/log stream.
- [ ] **6.2 — Dashboard ve Alert Kurulumu**
  - At least one Grafana dashboard for order volume, success/failure rate, and latency percentiles (p50/p95/p99).
  - Alerts: high lock contention, rising error rate, slower responses, and open circuit breaker.
- [ ] **6.3 — Eş Zamanlılık ve İdempotens Entegrasyon Testleri**
  - Verify that repeated requests with the same `Idempotency-Key` create one database record; use 5 requests in the test while keeping the design count-independent.
  - Close Phase 3.2 technical debt by validating `ReserveBatch` transaction rollback and concurrent reserve scenarios in Docker Compose with a real MongoDB replica set and Redis.
  - All-or-nothing: when 1 of 10 products is insufficient, no product state changes.
  - Confirm: `quantityReserved` decreases; `quantityAvailable` does not increase.
  - Release after cancel/expiry: `quantityReserved` decreases; `quantityAvailable` increases by the same amount.
  - `AdjustStock`: stock never becomes negative and every adjustment is written to the audit log.
- [ ] **6.4 — 100 Concurrent Request Stres Testi**
  - Simulate 100 simultaneous orders against the same SKU pool.
  - Separately test intersecting SKU batches where some batches share SKUs.
  - Also test intersecting SKU+warehouse sets in multi-warehouse mode.
  - Verify no deadlock, stale reservation, or overbooking in these scenarios.
- [ ] **6.5 — Çökme/Yeniden Başlama (Resilience) Testi**
  - Intentionally restart the `InventoryService` container during expiration polling or reservation; verify recovery continues without duplicate release.
- [ ] **6.6 — Graceful Degradation Testi**
  - Intentionally slow `InventoryService` until the circuit breaker opens; verify incoming create-order requests follow Phase 4.4 behavior: explicit rejection and no stale approval.
- [ ] **6.7 — Matematiksel Envanter Doğrulama Skripti**
  - Create the final verification script to run after all stress/load tests.
  - For every SKU+warehouse (`sku`, `warehouseId`), verify:
    `quantityAvailable + quantityReserved == Başlangıçtaki Toplam Stok + AdjustStock(delta toplamı) + Rebalance net etkisi`
  - Verify rebalance leaves the global total unchanged and changes only warehouse distribution.
  - Mathematically prove that no ghost inventory is created and no inventory is lost.

### Live Project State

- [x] **Phase 1:** Altyapı, Protokoller ve İzlenebilirlik Kurulumu
- [x] **Phase 2:** InventoryService Veri Modeli ve Dağıtık Kilit (Lock) Altyapısı
- [ ] **Phase 3:** InventoryService gRPC İş Mantığının Geliştirilmesi
- [ ] **Phase 4:** OrderService Sipariş Yönetimi ve Dirençli (Resilient) Entegrasyonlar
- [x] **Phase 5:** Otomatik Süre Aşımı (Expiry) ve Gelişmiş Operasyonel Özellikler
- [ ] **Phase 6:** İzlenebilirlik Panelleri, Doğrulama, Stres Testleri ve Kararlılık Kontrolleri

## 4. Status Automation & Known Gaps

- After every successful code implementation or test execution, update the relevant checklist (`[ ]` → `[x]`) and advance **Current Task**.
- Stop and request user confirmation before starting the next major task.
- `test/`, `scripts/`, Docker Compose, and proto files are not fully created yet; add them when implementation begins.
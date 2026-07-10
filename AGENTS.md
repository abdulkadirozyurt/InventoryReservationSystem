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

- [x] **6.1 — Envanter Tutarlılık Metriklerinin İzlenmesi**
  - `docker-compose.yml`: `OTEL_EXPORTER_OTLP_ENDPOINT=http://alloy:4317` her iki servise eklendi
  - `OTEL_SERVICE_NAME` ile servis isimleri ayarlandı; Prometheus job labels canlı ortamda `InventoryService` ve `OrderService` olarak doğrulandı
  - Alloy config: OTLP → Prometheus/Loki/Tempo pipeline çalışıyor
  - Compose runtime proof: `dotnet_*`, `http_server_*`, `http_client_*` metrikleri Prometheus'ta canlı sorgulandı
- [x] **6.2 — Dashboard ve Alert Kurulumu**
  - `observability/grafana/dashboards/inventory-reservation-overview.json` Docker servis ağı ana dashboard olarak güncellendi
  - `observability/grafana/dashboards/inventory-service-drilldown.json` ve `observability/grafana/dashboards/order-service-drilldown.json` servis detay dashboardları eklendi
  - Dashboard kapsamı: health, CPU, memory, request rate, error rate, latency, availability, custom inventory metrics, Redis locks, logs, telemetry gap notları
  - Alert'ler: high lock contention, rising 5xx, slow HTTP latency, open circuit breaker
  - Dashboard provisioning: `observability/grafana/provisioning/dashboards/dashboards.yml`
  - Datasource bağlantıları: Prometheus exemplars → Tempo, Loki derived field → Tempo, Tempo tracesToLogsV2 → Loki, Tempo tracesToMetrics/serviceMap → Prometheus
  - Audit fix: OTLP remote-write ortamında çalışmayan `up{...}` sorguları kaldırıldı; health panelleri canlı `dotnet_process_memory_working_set_bytes` metriğine bağlandı
  - Alloy pipeline'a batch processor eklendi; dashboard JSON ve Alloy format doğrulaması geçti
- [x] **6.3 — Eş Zamanlılık ve İdempotens Entegrasyon Testleri**
  - AppHost MongoDB ve Redis resource'larını ekledi, dependency graph tamamlandı
  - Aspire dashboard dependency health gösterir
  - `/health` ve `/health/ready` endpoint'leri dependency hazır olduğunda başarılı olur
  - Integration testleri oluşturuldu: `test/InventoryReservationSystem.IntegrationTests/`
- [x] **6.4 — Integration/E2E Testleri**
  - `InventoryReservationSystem.IntegrationTests.csproj` oluşturuldu
  - Testler: `ReserveBatchTests`, `ConfirmReservationTests`, `ReleaseBatchTests`, `AdjustStockTests`, `HealthCheckTests`
  - Build başarılı, Testcontainers ile çalışıyor
  - **Test sonucu: 9/9 geçti** (MongoDB replica set + Redis ile)
- [x] **6.5 — 100 Concurrent Request Stres Testi (k6 scripts)**
  - `scripts/stress/reservation-concurrency.js` — 100 concurrent VUs, 30s, shared SKU pool
  - `scripts/stress/intersecting-sku-batches.js` — 3 phases (5/30/80 VU), 3 intersecting patterns
  - `scripts/stress/expiry-pressure.js` — 20 VUs, create-cancel-recreate inventory recycling cycle
  - `scripts/stress/idempotency-retry.js` — concurrent duplicate keys + sequential replay scenarios
  - `scripts/stress/README.md` — usage instructions, prerequisites, metric tables
- [x] **6.6 — Restart/Degradation Testleri**
  - `scripts/resilience/restart-during-expiry.md` — container restart senaryosu, duplicate release yok, recovery var
  - `scripts/resilience/circuit-breaker-test.md` — circuit breaker open/close, 503 explicit failure, recovery proof
- [x] **6.7 — Matematiksel Envanter Doğrulama Skripti**
  - `scripts/verify-inventory-invariants.ps1` — 5 checks
  - Check 1: No negative quantities
  - Check 2: Per-(SKU,WH) initial stock >= 0 (state - ledger == >= 0)
  - Check 3: Rebalance is zero-sum per SKU globally
  - Check 4: Pending reservations total == qtyReserved per (SKU,WH)
  - Check 5: Non-pending reservations don't leak into reserved stock
  - Exit 0 (all pass) or 1 (any fail); per-(SKU,WH) detail output
  - Audit fix: host `mongosh` yoksa Docker Compose MongoDB container fallback kullanır
  - Runtime proof: Compose verisiyle invariant verifier 5/5 geçti

### Live Project State

- [x] **Phase 1:** Altyapı, Protokoller ve İzlenebilirlik Kurulumu
- [x] **Phase 2:** InventoryService Veri Modeli ve Dağıtık Kilit (Lock) Altyapısı
- [x] **Phase 3:** InventoryService gRPC İş Mantığının Geliştirilmesi
- [x] **Phase 4:** OrderService Sipariş Yönetimi ve Dirençli (Resilient) Entegrasyonlar
- [x] **Phase 5:** Otomatik Süre Aşımı (Expiry) ve Gelişmiş Operasyonel Özellikler
- [x] **Phase 6:** İzlenebilirlik Panelleri, Doğrulama, Stres Testleri ve Kararlılık Kontrolleri

## 4. Status Automation & Known Gaps

- After every successful code implementation or test execution, update the relevant checklist (`[ ]` → `[x]`) and advance **Current Task**.
- Stop and request user confirmation before starting the next major task.
- ~~`test/`, `scripts/`, Docker Compose, and proto files are not fully created yet~~ ✅ **FAZ 6 tamamlandı** — tüm test, script ve docker-compose asset'leri mevcut.
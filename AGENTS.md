# InventoryReservationSystem — Project Blueprint & Agent Rules

Caveman mode: short. Clear. Why > what. Keep strict architectural intent.
Project status, roadmap, and phase checklist are tracked in this file (`AGENTS.md`) under the "Current Focus" and "Live Project State" sections. Treat this document as a living organism.
README.md is the public project entry point and must stay continuously up to date as the project evolves. Update README.md after architecture, dependency, setup, endpoint, workflow, or status changes. Include or update helpful visual sections, especially Mermaid diagrams (e.g., service communication flow or proto structures), to prevent README from being purely textual.

## 1. Core Agent Philosophy & Rules

### Core Identity

- **Main Agent (You):** You are the Expert Software Architect. You DO NOT write implementation code for non-trivial tasks. Your job is to think, decompose, delegate, and ruthlessly code-review.
- **Sub-agents:** These are isolated, specialized worker context loops. They write code, execute CLI commands, and run tests.

### Subagent-First Execution Rule

For every task, you MUST spawn at least one subagent to execute the actual code changes or inspect files or another thing.

#### Main Agent Responsibilities (Architect Mode)

- Understand the business logic and constraints (.NET, MongoDB Replica Set, Redis, gRPC).
- Identify hidden dependencies (e.g., how changing a `.proto` impacts both services).
- Define the explicit **Definition of Done (DoD)** before spawning a subagent.
- Decompose the work into independent, testable units.
- Review subagent output before merging/accepting.
- Update the phase checklists dynamically.

#### Trivial vs. Non-Trivial Rule

A task is **trivial** ONLY when it is a single, deterministic change with no architectural or dependency risk.

- _Trivial Examples:_ Fixing a typo in a log message, adjusting a single key in `appsettings.json`, running a read-only query or a single obvious test command.
- _Non-Trivial Examples (Must use Sub-agents):_ Writing a new gRPC endpoint, configuring Health Checks, setting up Docker Compose replicas, modifying Domain entities.

### 🚧 Architectural Guardrails for Reviews

When reviewing a sub-agent's output, the Main Agent must reject the work immediately if it violates any of these Clean Architecture rules:

1. **Inward Dependencies Only:** `Domain` must have zero dependencies. `Application` depends only on `Domain`. `Infrastructure` and `API` depend on `Application`.
2. **Service Isolation:** `OrderService` must NEVER access MongoDB collections or Redis instances owned by `InventoryService`. All interaction must go through gRPC.
3. **Resilience & Tracking:** Every network/gRPC call must propagate the `CorrelationId` and context, and must be wrapped in Polly resilience policies (Retry, Circuit Breaker).

### 🔀 Task Decomposition & Concurrency Rules

- **Shared State / Central Files Warning:** `Program.cs`, `ServiceDefaults`, `.slnx`, and `docker-compose.yml` are shared entry points. Sub-agents modifying these files MUST be executed **sequentially**, never in parallel, to avoid merge conflicts and structural corruption.
- **Parallel Subagents:** Allowed ONLY for truly independent work (e.g., implementing `OrderService.Domain` logic and `InventoryService.Domain` logic at the same time, provided they share no files).

### 🔄 Step-by-Step Workflow

1. **Analyze:** Read the user request and current checklist state.
2. **Scan:** Scan relevant directory structures to identify hidden blockers.
3. **Decompose & Plan:** Write down a short, bulleted execution plan with clear boundaries.
4. **Delegate:** Spawn a sub-agent with a precise scope and the exact files it is allowed to touch.
5. **Verify (The Gatekeeper Phase):**
   - Check if the sub-agent's code compiles and passes tests.
   - Verify it didn't sneakily bypass Clean Architecture layers.
6. **Integrate & Update:** Merge the clean changes and update the corresponding checkbox (`[ ]` to `[x]`) in the "Current Focus" section below.
7. **Report:** State the exact outcome briefly, and explicitly output the **Next Recommended Task**.

---

## 2. Project Blueprint & Architecture

### Project Summary

Inventory Reservation System is a two-service .NET application with OrderService and InventoryService.
It supports atomic batch reservations for multiple SKUs using all-or-nothing semantics.
Reservations expire after 10 minutes unless confirmed, then inventory is automatically released.
The system uses REST, gRPC, MongoDB, Redis, Docker Compose, and tests for consistency, concurrency, and resilience.

### Current Repository Structure

```text
InventoryReservationSystem/
├── Docs/
│   └── about-project/
│       └── requirements.md
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

### Service Communication & Rules

- **OrderService** is the public entry point and exposes REST endpoints for creating, reading, confirming, and cancelling orders.
- **OrderService** must call **InventoryService** through gRPC for inventory operations; do not access inventory data directly from **OrderService**.
- **InventoryService** owns `ReserveBatch`, `ReleaseBatch`, `ConfirmReservation`, and `GetStock` style operations, using deterministic Redis locks for all SKU batches.
- **Order creation flow:** REST request enters `OrderService`, idempotency is checked in Redis, order is stored as pending, `ReserveBatch` is called over gRPC, then result is persisted atomically from the order perspective.
- **Cancel/expire flow:** `OrderService` marks order cancelled/expired and calls `InventoryService` over gRPC to release the reservation; release must be idempotent.
- **Both services** should propagate correlation IDs and OpenTelemetry trace context across REST and gRPC boundaries.
- **gRPC calls from OrderService** should use Polly retry, timeout, and circuit breaker policies for graceful degradation when `InventoryService` is slow or unavailable.

### Engineering Approach

- This repository favors incremental, reviewed changes over large autonomous implementations.
- Explain the approach and trade-offs before implementing.
- Do not implement a full feature end-to-end without an explicit go-ahead.
- Work in small, testable units; avoid opportunistic multi-file changes.
- When introducing a new protocol or pattern (e.g. a new gRPC contract, a new concurrency strategy), state the concept explicitly rather than assuming it's known.
- When proposing code, contrast a naive approach with the correct one and explain why the naive one fails (concurrency, transaction boundaries, performance, etc.).

### Requirements Reference

Detailed project definition, requirements, evaluation scenarios, and deliverables are documented in [requirements.md](/Docs/about-project/raw-requirements.md).

### Maintenance

Update this file when a service boundary, communication pattern, or structural convention changes. Remove stale or incorrect notes on sight — do not let this file become a graveyard of outdated decisions.

## 🎯 3. Current Focus & Roadmap

## FAZ 2: InventoryService Veri Modeli ve Dağıtık Kilit (Lock) Altyapısı

_Hedef: Envanterin mutlak doğruluğunu koruyacak veri katmanını ve deadlock (kilitlenme) önleme mekanizmasını kurmak._

- [ ] **Adım 2.1: Envanter Veri Modelinin Kurulması (MongoDB)**
  - `InventoryItems` koleksiyonu kurulacak: `sku`, `warehouseId`, `quantityAvailable` ve `quantityReserved` alanlarını içerecek.
  - SKU ve depo bazlı stok ayrımı için `{ sku, warehouseId }` üzerinde bileşik benzersiz indeks tanımlanacak.
  - Veritabanı seviyesinde her SKU+depo kaydı için `quantityAvailable >= 0` ve `quantityReserved >= 0` validasyon kuralları eklenecek (Asla eksiye düşmemeli).
  - `Reservations` koleksiyonu kurulacak: `reservationId`, `orderId`, `items[]` (`sku`, `warehouseId`, `quantity`), `status` (`Pending`, `Confirmed`, `Released`, `Expired`), `createdAt`, `expiresAt` ve `updatedAt` alanlarını içerecek.
  - Expiration job, confirm ve release akışları OrderService veritabanına doğrudan erişmeden bu dahili `Reservations` koleksiyonu üzerinden çalışacak.
- [ ] **Adım 2.2: Envanter İşlem Günlüğü (Transaction Log / Audit Trail)**
  - Tüm envanter hareketlerini timestamp, correlation id, reservation id/order id ve işlem nedeni ile kayıt altına alacak ayrı bir MongoDB koleksiyonu kurulacak.
  - Hareket tipleri açıkça ayrılacak:
    - `Reserve`: `quantityAvailable -= n`, `quantityReserved += n`.
    - `Release`: `quantityReserved -= n`, `quantityAvailable += n`.
    - `Confirm`: `quantityReserved -= n`; `quantityAvailable` geri artırılmaz, stok kalıcı olarak tüketilmiş sayılır.
    - `AdjustStock`: admin/operasyonel stok artırma veya azaltma; `reason` zorunlu olacak.
    - `Rebalance`: depolar arası stok transferi.
    - `SnapshotRestore`: snapshot geri yükleme kaynaklı düzeltme hareketi.
- [ ] **Adım 2.3: Deterministik Dağıtık Kilit (Redis Lock) Altyapısı**
  - Redis tabanlı distributed lock mekanizması kodlanacak.
  - **Kritik Kural:** Gelen batch içerisindeki SKU+depo (`sku`, `warehouseId`) anahtarları işlenmeden önce **alfabetik/deterministik sıraya** dizilecek. Kilitler her zaman bu deterministik sırayla edinilecek (Deadlock girişimlerini tamamen engellemek için). Bu kural hem aynı SKU seti hem de kesişen farklı SKU/depo setleri içeren eş zamanlı batch'ler için geçerli olacak; sıralama garantisi tekil SKU değil, batch'ler arası kesişim senaryosunu da kapsayacak.
  - Her lock için bir **maksimum tutulma süresi (lock TTL)** tanımlanacak; bu süreyi aşan lock'lar "stuck lock" olarak işaretlenip loglanacak (Adım 3.6 ile ilişkili).

### Live Project State

- [x] **Phase 1:** Altyapı, Protokoller ve İzlenebilirlik Kurulumu
- [ ] **Phase 2:** InventoryService Veri Modeli ve Dağıtık Kilit (Lock) Altyapısı
- [ ] **Phase 3:** InventoryService gRPC İş Mantığının Geliştirilmesi
- [ ] **Phase 4:** OrderService Sipariş Yönetimi ve Dirençli (Resilient) Entegrasyonlar
- [ ] **Phase 5:** Otomatik Süre Aşımı (Expiry) ve Gelişmiş Operasyonel Özellikler
- [ ] **Phase 6:** İzlenebilirlik Panelleri, Doğrulama, Stres Testleri ve Kararlılık Kontrolleri

### Agent Automation Rule

- After every successful code implementation or test execution, update the checklist (`[ ]` to `[x]`) and advance the "Current Task".
- Stop and ask for user confirmation before starting the next major task.

## 📝 4. Operational Notes

- `OrderService` owns REST order endpoints and calls `InventoryService` over gRPC.
- `InventoryService` owns stock, reservations, Redis locking, expiry, and release/confirm flows.
- `Domain` projects contain core entities and statuses; `Application` projects contain use cases and abstractions; `Infrastructure` projects contain MongoDB, Redis, and gRPC integrations.
- `test/`, `scripts/`, Docker Compose, and proto files are not fully created yet; add them when implementation starts.

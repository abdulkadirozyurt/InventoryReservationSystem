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
4. **Polly Scope Discipline:** Proje genelinde retry, timeout, circuit breaker ve rate limit gibi resilience ihtiyaçları uygun olduğunda Polly pipeline ile modellenmelidir. Polly stratejisi senaryoya göre seçilmelidir: retry/timeout transient dependency hataları ve kontrollü beklemeler için; circuit breaker gerçek dış bağımlılık arızaları için; rate limit ise giriş/çıkış trafiğini sınırlamak için kullanılmalıdır. Normal business contention durumları (ör. Redis lock contention) circuit breaker açma sebebi sayılmamalıdır.

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
   - Every feature must be Docker-built and verified immediately in the running Compose environment before starting the next feature or marking a checklist item complete.
   - Runtime verification must include the smallest end-to-end request that exercises the changed path, relevant container logs, and MongoDB/Redis state checks when the feature writes data or uses locks.
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

### Logging & Audit Trail Rules

- **Structured Serilog Only:** Proje genelindeki tüm teknik uygulama logları Serilog üzerinden structured logging olarak yazılmalıdır.
- **Message Template Zorunlu:** Log mesajları message template ve named property kullanmalıdır; string interpolation (`$"..."`) veya manuel string birleştirme kullanılmamalıdır.
  - _Yanlış:_ `_logger.LogInformation($"Order {orderId} processed.");`
  - _Doğru:_ `_logger.LogInformation("Order {OrderId} processed.", orderId);`
- **Context Zorunlu:** Runtime loglarda mümkün olduğunca `CorrelationId`, `OrderId`, `ReservationId`, `Sku`, `WarehouseId`, `LockKey`, `ElapsedMs` ve hata kategorisi gibi aranabilir alanlar named property olarak taşınmalıdır.
- **Audit Trail Ayrı Kayıttır:** Audit trail kayıtları (`InventoryTransactions`, `OrderHistory` vb.) teknik log değildir; domain davranışını kanıtlayan kalıcı veri kayıtlarıdır ve ilgili runtime akışıyla birlikte yazılmalıdır.

### Requirements Reference

Detailed project definition, requirements, evaluation scenarios, and deliverables are documented in [requirements.md](/Docs/about-project/raw-requirements.md).

### Maintenance

Update this file when a service boundary, communication pattern, or structural convention changes. Remove stale or incorrect notes on sight — do not let this file become a graveyard of outdated decisions.

## 🎯 3. Current Focus & Roadmap

## FAZ 4: OrderService Sipariş Yönetimi ve Dirençli (Resilient) Entegrasyonlar
*Hedef: Dış dünyaya açık kapıyı oluşturmak ve iç servis yavaşlıklarına karşı sistemi korumak.*

- [X] **Adım 4.1: Sipariş Veri Modeli ve Tarihçe (MongoDB)**
  - `Order` ve `OrderLineItem` (sku, requested quantity, reserved quantity, warehouseId) şemaları tasarlandı.
  - Domain tarafında kafa karışıklığını önlemek için sipariş dış kimliği `OrderNumber`, InventoryService rezervasyon bağı ise `reservationId` olarak ayrıldı.
  - OrderService MongoDB üzerinde `orders` ve `order-history` koleksiyonları collection validation ve indexlerle oluşturuluyor.
  - Tüm durum geçişlerini (`Pending`, `Confirmed`, `Cancelled`, `Expired`) timestamp ile kaydeden `OrderHistory` audit trail modeli kuruldu.
  - Order status değişimleri için timestamp, correlation id ve değişim nedeni alanları hazırlandı; endpoint business akışında kullanılması Adım 4.2 kapsamına bırakıldı.
- [X] **Adım 4.2: Minimal API İskeletinin Kurulması**
  - Create Order, Get Order, Cancel Order, Bulk Cancel (Tek istekte çoklu iptal), List Orders (Status ve tarih filtresiyle) ve Order Confirmation endpoint'leri boş fonksiyonlar olarak tanımlanacak.
  - Order Confirmation endpoint'i, order `Confirmed` yapılmadan önce InventoryService üzerinde `ConfirmReservation(reservationId)` çağıracak.
  - Cancel Order ve Bulk Cancel endpoint'leri her order'ın `reservationId` değeri için `ReleaseBatch` çağıracak; bulk cancel tek HTTP isteği olsa bile InventoryService tarafında release işlemi rezervasyon bazında idempotent kalacak.
  - Create, cancel, bulk cancel, confirm ve get/list endpointlerinde request başlangıç/bitiş, hata, correlation id ve dış gRPC çağrı sonucu technical log olarak yazılacak.
- [X] **Adım 4.3: Redis Tabanlı İdempotency Katmanı**
  - Create Order endpoint'ine gelen isteklerin `Idempotency-Key` başlığı Redis üzerinde kontrol edilecek.
  - Aynı isteğin **her tekrarında** (tekrar sayısı sınırsız — 5 kez, 50 kez fark etmez), veritabanına veya gRPC'ye tekrar gitmeden hafızadaki aynı sonuç doğrudan dönülecek. ("5 keze kadar" gibi bir üst sınır yok; idempotency garantisi tekrar sayısından bağımsız olacak.)
  - Idempotency hit/miss, replay response, key conflict, Redis timeout ve cache write/read failure durumları correlation id ile loglanacak.
- [X] **Adım 4.4: Polly ile gRPC İletişiminin Güvenli Hale Getirilmesi**
  - OrderService, InventoryService'i ararken `ReserveBatch`, `ReleaseBatch`, `ConfirmReservation` ve mevcut operasyonel envanter çağrıları için ortak `InventoryGrpcResilienceExecutor` pipeline'ını kullanıyor.
  - InventoryService tarafında global `GrpcExceptionInterceptor` etkin; `InventoryStoreUnavailableException`, `DuplicateReservationException`, `TimeoutException`, `ArgumentException` ve beklenmeyen hatalar safe gRPC status code'larına mapleniyor.
  - OrderService tarafında dependency exception mapping merkezi `OrderServiceExceptionHandler` ile yapılıyor: circuit open ve unavailable -> 503, timeout/deadline/cancelled timeout -> 504, conflict -> 409, beklenmeyen downstream gRPC -> 502.
  - Geçici hatalar için retry, yavaş çağrılar için per-attempt timeout, sürekli arızalar için circuit breaker aktif. Defaults: 3sn timeout, 3 retry, 200ms base delay, %50 failure ratio, 30sn sampling, 15sn break duration.
  - Retry, timeout, circuit breaker ve idempotency claim release akışları structured log üretiyor; runtime doğrulamada claim release ve circuit-open fail-fast logları gözlendi.
  - **Graceful Degradation davranışı doğrulandı:** InventoryService unavailable iken create-order 503 dönüyor ve Mongo'da Pending order oluşmuyor; InventoryService yavaş/paused iken 504 dönüyor; circuit breaker open durumunda fail-fast 503 dönüyor; transient failure sonrası Redis Processing claim atomic Lua script ile release ediliyor ve aynı Idempotency-Key başarılı şekilde retry edilebiliyor.

### Live Project State

- [x] **Phase 1:** Altyapı, Protokoller ve İzlenebilirlik Kurulumu
- [x] **Phase 2:** InventoryService Veri Modeli ve Dağıtık Kilit (Lock) Altyapısı
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

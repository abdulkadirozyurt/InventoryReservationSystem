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

## FAZ 3: InventoryService gRPC İş Mantığının Geliştirilmesi
*Hedef: All-or-nothing prensibine sahip atomik rezervasyon motorunu inşa etmek.*

- [x] **Adım 3.1: GetStock(sku) Metodunun Yazılması**
  - Verilen SKU'ya ait güncel envanter durumunu dönen servis kodu yazılacak.
  - `warehouseId` verilirse tek depo, boş verilirse SKU'nun toplam/genel stok görünümü dönecek şekilde davranış netleştirilecek.
  - Stok sorgularında correlation id ile technical log üretilecek; bulunamayan SKU/depo, geçersiz istek ve transient Mongo hataları ayrı kategorilerle loglanacak.
- [x] **Adım 3.2: ReserveBatch(items[]) Metodunun Yazılması (All-or-Nothing)**
  - Deterministik SKU+depo kilit sırasına göre Redis üzerinde kilitler edinilecek.
  - Batch içerisindeki tüm ürünlerin stokları kontrol edilecek. **Eğer tek bir ürünün bile stoku yetersizse, işlem anında iptal edilecek**, edinilen kilitler serbest bırakılacak ve hata dönülecek (Kısmi rezervasyona izin yok).
  - Tüm ürünlerin stoku yeterliyse, ilgili miktarlar `quantityAvailable` alanından düşülüp `quantityReserved` alanına atomik olarak eklenecek.
  - Başarılı işlemde `Reservations` koleksiyonuna `Pending` durumunda, `expiresAt = createdAt + 10 dakika` olacak şekilde rezervasyon kaydı yazılacak.
  - Başarılı rezervasyonlarda `InventoryTransactions` koleksiyonuna `Reserve` audit kaydı yazılacak.
  - Stok yetersizliği, lock timeout, transaction rollback, cancellation ve transient Mongo/Redis hataları correlation id ile technical log olarak yazılacak.
  - Aynı SKU'ları veya kesişen SKU/depo setlerini hedefleyen eş zamanlı batch'lerin, lock sırası sayesinde birbirini bekleyip sırayla işlenmesi ve hiçbirinin overbooking'e yol açmaması garanti altına alınacak.
- [ ] **Adım 3.3: ReleaseBatch(items[]) Metodunun Yazılması**
  - Aynı deterministik SKU+depo kilit sırasını kullanarak rezervasyonları serbest bırakacak (Stoku geri iade edecek) metot yazılacak.
  - `Release` hareketinde `quantityReserved` azaltılacak ve `quantityAvailable` aynı miktarda artırılacak.
  - `Reservations` kaydı `Released` veya expiry akışından geliyorsa `Expired` durumuna geçirilecek.
  - Başarılı release/expiry işlemlerinde `InventoryTransactions` koleksiyonuna `Release` audit kaydı yazılacak.
  - Duplicate/idempotent release denemeleri, release edilmeyen rezervasyonlar, lock timeout ve transient hatalar correlation id ile loglanacak.
  - **İdempotent Kontrol:** Aynı rezervasyonun ikinci kez release edilmesi Redis ve/veya `Reservations.status` üzerinden kontrol edilerek engellenecek.
- [ ] **Adım 3.4: ConfirmReservation(reservationId) Metodunun Yazılması**
  - `reservationId` ile dahili `Reservations` kaydı bulunacak ve durumun `Pending` olduğu doğrulanacak.
  - Rezervasyon içindeki SKU+depo anahtarları deterministik sırayla lock edilecek.
  - Confirm işleminde yalnızca `quantityReserved` azaltılacak; `quantityAvailable` geri artırılmayacak.
  - Rezervasyon `Confirmed` durumuna geçirilecek ve transaction log'a `Confirm` hareketi yazılacak.
  - Başarılı confirm işlemlerinde `InventoryTransactions` koleksiyonuna `Confirm` audit kaydı yazılacak.
  - Duplicate confirm, geçersiz reservation state, lock timeout ve transient hatalar correlation id ile loglanacak.
  - Aynı rezervasyonun tekrar confirm edilmesi idempotent kabul edilecek; duplicate confirm stok miktarını ikinci kez değiştirmeyecek.
- [ ] **Adım 3.5: AdjustStock(sku, warehouseId, delta, reason) Metodunun Yazılması**
  - Bu metot sadece admin/operasyonel envanter düzeltmeleri için kullanılacak; order rezervasyon akışının parçası olmayacak.
  - İlgili SKU+depo anahtarı deterministik lock ile korunacak.
  - `delta` pozitifse stok artırma, negatifse stok azaltma yapılacak; işlem sonrası `quantityAvailable` ve `quantityReserved` negatif olamayacak.
  - `reason` zorunlu tutulacak ve her işlem transaction log'a `AdjustStock` hareketi olarak yazılacak.
  - Her `AdjustStock` işleminde reason, correlation id, SKU/depo ve stok delta bilgisiyle `InventoryTransactions` audit kaydı yazılacak.
  - Negatif stok engeli, validation failure, lock timeout ve admin düzeltmeleri technical log olarak yazılacak.
- [ ] **Adım 3.6: Metriklerin Enjeksiyonu ve Log Sınıflandırması**
  - Önceki adımlarda eklenen lock, reservation, release, confirm, adjust stock ve hata logları OpenTelemetry metrikleriyle ilişkilendirilecek.
  - Lock contention, lock ownership süresi, timeout sayısı ve transient/sistemsel hata ayrımı metrik olarak üretilecek.
  - Adım 2.3'te tanımlanan lock TTL aşımları ayrı bir metrik/log olarak işaretlenecek (**"beklenenden uzun tutulan lock" tespiti**).
  - Bu adım feature loglarını ilk kez eklemek için değil, mevcut runtime sinyallerini ölçülebilir hale getirmek için kullanılacak.

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

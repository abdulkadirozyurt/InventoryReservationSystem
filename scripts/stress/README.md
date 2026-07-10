# Stress Tests — Inventory Reservation System

k6 scripts for load, contention, idempotency, and inventory recycling testing against the Inventory Reservation System.

## Prerequisites

- [k6](https://k6.io/docs/getting-started/installation/) v0.47+ installed
- System running (see below)

## Starting the System

From the repository root:

```bash
docker-compose up -d
```

The OrderService REST API is available at `http://localhost:5032` (port 5032, mapped from internal port 8080).

Run `docker-compose ps` to verify all services are healthy before starting tests.

## Running the Tests

Run scripts from the repository root:

```bash
k6 run scripts/stress/reservation-concurrency.js
```

### Overriding Options

Pass k6 CLI flags to override VUs, duration, or other options:

```bash
k6 run --vus 50 --duration 120s scripts/stress/reservation-concurrency.js
```

### Overriding Base URL

Set `BASE_URL` environment variable if the API is on a different host or port:

```bash
# Windows (PowerShell)
$env:BASE_URL="http://localhost:5032"; k6 run scripts/stress/reservation-concurrency.js

# Windows (cmd)
set BASE_URL=http://localhost:5032&& k6 run scripts/stress/reservation-concurrency.js

# Linux/macOS
BASE_URL=http://localhost:5032 k6 run scripts/stress/reservation-concurrency.js
```

## Test Scripts

### 1. `reservation-concurrency.js` — Reservation Concurrency

**Profile:** Smoke / Contention

**Behavior:**
- 100 concurrent VUs over 30 seconds
- Each VU creates an order with 1-3 random items from a shared pool (SKU-001, SKU-004, SKU-005, SKU-010)
- Order is immediately confirmed
- Tests atomic batch reservation under high concurrency

**What to check:**
- No overbooking: total successful reservations must not exceed available inventory per SKU
- Threshold: http_req_failed rate < 10% (failures from stock exhaustion are expected)
- Console output shows INSUFFICIENT_STOCK errors when contention exhausts a SKU

**Run:**
```bash
k6 run scripts/stress/reservation-concurrency.js
```

### 2. `intersecting-sku-batches.js` — Intersecting SKU Batches

**Profile:** Smoke (5 VU) -> Normal (30 VU) -> Overload (80 VU)

**Behavior:**
- Three load phases: smoke (20s), normal (30s), overload (20s), cooldown (10s)
- Three batch patterns run simultaneously (VU modulo selection):
  1. **Non-intersecting**: each order uses unique SKU combos (SKU-005 through SKU-010)
  2. **Fully intersecting**: all orders compete for SKU-001 and SKU-002
  3. **Partially intersecting**: SKU-001 shared, second SKU unique
- Confirmed orders verified via GET to ensure correct state

**What to check:**
- Per-pattern success rates (tagged in metrics as `pattern` tag)
- SKU contention under different intersection levels
- GET verification confirms reserved quantities are accurate
- Threshold: http_req_failed rate < 15%

**Run:**
```bash
k6 run scripts/stress/intersecting-sku-batches.js
```

### 3. `expiry-pressure.js` — Expiry / Cancellation Pressure

**Profile:** Steady load (20 VU) over 60s

**Behavior:**
- Ramp-up to 20 VUs over 10s, steady for 40s, ramp-down over 10s
- Each VU cycle: create order -> cancel order -> recreate with same items
- Uses SKU-003 (only 3 available) mixed with high-availability SKUs
- Tests that cancelled orders release inventory for immediate reuse

**What to check:**
- Cancel success rate: all created orders should cancel successfully
- Recreate success rate: after cancel, recreating with same SKUs should succeed
- Console output shows "inventory recycled" for successful recreate, or "inventory NOT recycled" for failures
- Threshold: http_req_failed rate < 20%

**Run:**
```bash
k6 run scripts/stress/expiry-pressure.js
```

### 4. `idempotency-retry.js` — Idempotency and Retry

**Profile:** Two scenarios running sequentially

**Behavior:**
- **Scenario A (concurrent duplicates)** — `per-vu-iterations` executor, 10 VUs, 5 iterations:
  - Sends 3-5 requests with the same Idempotency-Key simultaneously via `http.batch()`
  - Asserts: exactly 1 succeeds (200), remaining return 409 Conflict
- **Scenario B (sequential replay)** — `per-vu-iterations` executor, 5 VUs, 3 iterations:
  - Sends first request, waits, sends same Idempotency-Key again
  - Asserts: second request returns 200 with same orderNumber (cached replay, not 409)

**What to check:**
- No duplicate orders created for the same idempotency key
- 409 Conflict returned for in-flight duplicate keys
- 200 OK with same body for completed idempotency key replay
- Number of created orders = number of unique idempotency keys
- Threshold: http_req_failed rate < 5%

**Run:**
```bash
k6 run scripts/stress/idempotency-retry.js
```

## Metrics to Monitor

During stress tests, watch these metrics via k6 output or system logs:

| Metric | Where | What to look for |
|---|---|---|
| `http_req_failed` | k6 output | Rate should stay below script thresholds |
| `http_req_duration` | k6 output | P(95) latency under load |
| `INSUFFICIENT_STOCK` | Console log | Expected under contention — track frequency |
| `IdempotencyKeyConflict` | Console log | Expected for concurrent duplicate key tests |
| Cancel success rate | Console log | Should approach 100% |
| Recreate success rate | Console log | Should approach 100% after cancel |
| System logs | docker-compose logs | Check OrderService and InventoryService for errors |

## Viewing k6 Results

For detailed metrics output:

```bash
k6 run --summary-trend-stats="avg,p(50),p(90),p(95),p(99)" scripts/stress/reservation-concurrency.js
```

For JSON output (useful for CI):

```bash
k6 run --summary-export=results.json scripts/stress/reservation-concurrency.js
```

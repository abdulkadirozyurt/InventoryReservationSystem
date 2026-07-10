# Circuit Breaker Test — Open/Close Resilience Scenario

## Purpose

Verify that when `InventoryService`'s downstream dependency (MongoDB / external API) becomes slow, the circuit breaker opens, new requests fail-fast with `503`, and the circuit auto-recovers when the dependency heals.

## Preconditions

- Polly `CircuitBreakerAsyncPolicy` registered in `HttpClient` or DI pipeline (target: DB connection or external stock API).
- Circuit breaker configured:
  - `HandledEventsAllowedBeforeBreaking`: 3
  - `DurationOfBreak`: 30s
- Health endpoint `/health` returns circuit state info.

## Steps

### 1. Verify baseline — circuit closed

```bash
# Warm up — send healthy requests:
for i in 1 2 3; do
  curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/api/reservations
  echo ""
done
```

```bash
# Check circuit state via health:
curl -s http://localhost:5000/health | jq '.circuitBreaker'
```

Expected: `"Closed"` or `"HalfOpen"`.

### 2. Inject latency into dependency

Option A — MongoDB slow query via `sleep` command:

```bash
mongosh --quiet --eval 'db.adminCommand({sleep: 1, lock: "none", seconds: 10})'
# Adjust so every reservation query hits a 10s delay.
```

Option B — Network delay via `tc` (if running containers locally):

```bash
docker exec inventory-service tc qdisc add dev eth0 root netem delay 10000ms 1000ms
```

Option C — Application-level slow-down (dev only): set env var `SIMULATE_SLOW_DB=true` and restart service.

### 3. Trigger failures to open the circuit

Send requests until breaker opens:

```bash
for i in 1 2 3 4 5; do
  echo "Request $i:"
  curl -s -o /dev/null -w "  HTTP %{http_code}\n" -X POST \
    http://localhost:5000/api/reservations \
    -H "Content-Type: application/json" \
    -d '{"sku":"TEST-SKU","quantity":1}'
done
```

Expected output (from request 4 or 5 onward):

```
Request 1:  200
Request 2:  200
Request 3:  200      # 3 consecutive slow calls → counters trip
Request 4:  503      # CircuitOpenException
Request 5:  503
```

### 4. Confirm all subsequent requests fail fast (no silent accepts)

```bash
# Send 10 requests in parallel — all must be 503, none 200
for i in $(seq 1 10); do
  curl -s -w "\n%{http_code}" http://localhost:5000/api/reservations \
    -o /dev/null
done | grep -v '^$' | sort | uniq -c
```

Expected:

```
     10 503
```

If any `200` or `201` appears, circuit breaker is not blocking writes.

### 5. Remove latency injection

```bash
# Option B cleanup:
docker exec inventory-service tc qdisc del dev eth0 root netem

# Option C cleanup:
docker rm -f inventory-service
# restart with SIMULATE_SLOW_DB unset
```

### 6. Wait for half-open and recovery

```bash
# Poll health until circuit closes:
for i in $(seq 1 60); do
  state=$(curl -sf http://localhost:5000/health | jq -r '.circuitBreaker')
  echo "$(date +%T) state=$state"
  [ "$state" = "Closed" ] && break
  sleep 2
done
```

Expected: after `DurationOfBreak` (30s), Polly transitions to `HalfOpen`, sends a probe request, and if successful, closes the circuit.

### 7. Verify normal operation resumes

```bash
curl -s -o /dev/null -w "HTTP %{http_code}\n" \
  -X POST http://localhost:5000/api/reservations \
  -H "Content-Type: application/json" \
  -d '{"sku":"TEST-SKU","quantity":1}'
```

Expected: `200`.

## Log / Dashboard Queries

### Circuit open count over time (Grafana / Kibana)

```
level:error AND message:"Circuit breaker opened"
```

```
level:error AND message:"CircuitBreakerOpenException"
```

### Fail-fast ratio (Kibana query)

```
kubernetes.container_name: inventory-service
AND http.status_code: 503
AND @timestamp > now-5m
```

### Half-open probe success

```
level:info AND message:"Circuit breaker half-open — probe request"
```

Add this log line to your Polly `OnHalfOpen` handler.

## Expected Outcome

- Circuit opens after N consecutive failures (N = `HandledEventsAllowedBeforeBreaking`).
- All requests return `503 Service Unavailable` while open — no request silently succeeds.
- After break duration expires, circuit transitions to `HalfOpen` and probes.
- On probe success, circuit closes and normal flow resumes.
- Dashboard shows spike of `503` during break, then return to `200`.

# Restart During Expiry Polling — Container Crash Recovery Test

## Purpose

Verify that restarting `InventoryService` container while the expiry background job is mid-poll does not produce duplicate releases and the service recovers cleanly.

## Preconditions

- Cluster running, `InventoryService` healthy, expiry job configured to run every 5s.
- At least 3 expired pending reservations exist (created > expiry window ago).
- Idempotency logs visible: `"ExpiredReservationsProcessed"`, `"SkippedReservationAlreadyReleased"`.

## Steps

### 1. Seed expired reservations

```bash
# Direct DB insert or wait for natural expiry.
# Confirm count:
docker exec mongo mongosh --quiet --eval '
  db.getSiblingDB("inventory_reservations")
   .getCollection("reservations")
   .countDocuments({ status: "Pending", expiresAt: { $lt: new Date() } })
'
```

Expect >= 3.

### 2. Start log tail for duplicate detection

```bash
docker logs -f --since 5m $(docker ps -q --filter name=inventory-service) \
  | grep -E 'ExpiredReservationsProcessed|SkippedReservationAlreadyReleased|ReservationReleased|ERROR' \
  > restart-test.log &
```

### 3. Force container restart (mid-poll window)

```bash
# Wait for an "ExpiredReservationsProcessed" heartbeat, then kill immediately.
docker restart $(docker ps -q --filter name=inventory-service) --time 1
```

### 4. Wait for recovery

```bash
# Poll until service returns healthy:
until curl -sf http://localhost:5000/health; do sleep 1; done
echo "Service recovered"
```

### 5. Let expiry job run 2+ cycles after recovery

```bash
sleep 15
```

## Verification

### No duplicate releases

Search for reservation IDs released more than once:

```bash
grep -oP 'ReservationReleased\s+\S+' restart-test.log \
  | sort | uniq -c | awk '$1 > 1 { print "DUPLICATE: " $2 }'
```

Expected: **empty output**.

### Recovery completes

```bash
grep -c 'ExpiredReservationsProcessed' restart-test.log
```

Expected >= 2 after sleep. If 0, service never resumed expiry processing.

### Error count

```bash
grep -c 'ERROR' restart-test.log
```

Expected: 0 or minimal (connection errors during kill are acceptable).

## Cleanup

```bash
rm restart-test.log
```

## Expected Outcome

- No reservation appears in more than one release log line.
- Service resumes processing expired reservations within one job interval of restart.
- Any in-flight release at kill time is rolled back by MongoDB transaction or skipped on next poll via idempotency check.

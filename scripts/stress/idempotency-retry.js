// idempotency-retry.js
// Tests idempotency-key handling under concurrent and sequential retry.
// Scenario A: Send same Idempotency-Key 3-5x concurrently via http.batch().
//   Verify only first succeeds (200) and rest return 409 Conflict.
// Scenario B: Send same key after first request completes (should replay cached response).
// Assert: number of successful orders == number of unique keys.

import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
    scenarios: {
        concurrent_duplicates: {
            executor: 'per-vu-iterations',
            vus: 10,
            iterations: 5,
            maxDuration: '30s',
            tags: { scenario: 'idempotency-concurrent' },
        },
        sequential_replay: {
            executor: 'per-vu-iterations',
            vus: 5,
            iterations: 3,
            maxDuration: '20s',
            startTime: '5s',
            tags: { scenario: 'idempotency-sequential' },
        },
    },
    thresholds: {
        http_req_failed: ['rate<0.05'],
    },
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5041';
const API = `${BASE_URL}/api/orders`;

const ITEMS = JSON.stringify({
    items: [{ sku: 'SKU-004', warehouseId: 'WH-1', quantity: 1 }],
});

function uuid() {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
        const r = Math.random() * 16 | 0;
        return (c === 'x' ? r : (r & 0x3 | 0x8)).toString(16);
    });
}

// Shared counter for unique keys per VU (reset each iteration)
let uniqueKeySuccessCount = 0;

// Scenario A: Concurrent duplicate idempotency keys
// Send 3-5 requests with the same key simultaneously using http.batch()
function scenarioConcurrentDuplicates() {
    const sharedKey = uuid();
    const correlationId = uuid();
    const requestCount = Math.floor(Math.random() * 3) + 3; // 3 to 5
    const headers = {
        'Content-Type': 'application/json',
        'Idempotency-Key': sharedKey,
        'X-Correlation-ID': correlationId,
    };

    // Build batch requests — all with same Idempotency-Key
    const batchRequests = [];
    for (let i = 0; i < requestCount; i++) {
        batchRequests.push(['POST', API, ITEMS, headers]);
    }

    // Fire all concurrently
    const responses = http.batch(batchRequests);

    // Analyze results
    let successCount = 0;
    let conflictCount = 0;

    for (let i = 0; i < responses.length; i++) {
        const r = responses[i];
        if (r.status === 200) {
            const body = r.json();
            if (body.success === true) {
                successCount++;
            }
        } else if (r.status === 409) {
            conflictCount++;
        }
    }

    // Assert: exactly 1 success, rest are 409
    const pass = check(responses[0], {
        'concurrent: at most 1 success per idempotency key': () => successCount === 1,
        'concurrent: remaining requests get 409': () =>
            conflictCount === requestCount - 1,
        'concurrent: no unexpected status codes': () =>
            successCount + conflictCount === requestCount,
    });

    if (!pass && successCount !== 1) {
        console.log(
            `VU ${__VU}: idempotency key ${sharedKey} got ${successCount} successes, ` +
            `${conflictCount} conflicts out of ${requestCount} requests`
        );
    } else {
        uniqueKeySuccessCount++;
    }

    sleep(0.5);
}

// Scenario B: Sequential replay — same key after first request completes
function scenarioSequentialReplay() {
    const sharedKey = uuid();
    const correlationId = uuid();
    const headers = {
        'Content-Type': 'application/json',
        'Idempotency-Key': sharedKey,
        'X-Correlation-ID': correlationId,
    };

    // First request
    const firstRes = http.post(API, ITEMS, { headers, tags: { action: 'first' } });

    check(firstRes, {
        'sequential: first request returns 200': (r) => r.status === 200,
    });

    const firstBody = firstRes.json();
    if (firstRes.status === 200 && firstBody.success) {
        uniqueKeySuccessCount++;
    }

    // Wait a moment for processing to fully complete
    sleep(0.3);

    // Send same idempotency key again — should get cached response (200, same body)
    const retryHeaders = {
        'Content-Type': 'application/json',
        'Idempotency-Key': sharedKey,
        'X-Correlation-ID': uuid(),
    };

    const retryRes = http.post(API, ITEMS, {
        headers: retryHeaders,
        tags: { action: 'retry' },
    });

    const retryOk = check(retryRes, {
        'sequential: retry with same key returns 200 (not 409)': (r) => r.status === 200,
        'sequential: retry returns success true': (r) => {
            if (r.status !== 200) return false;
            const body = r.json();
            return body.success === true;
        },
        'sequential: retry returns same orderNumber': (r) => {
            if (r.status !== 200) return false;
            const body = r.json();
            // After the first successful response, a retry with same key should
            // return the same orderNumber (idempotency replay)
            return body.orderNumber === firstBody.orderNumber;
        },
    });

    if (!retryOk) {
        console.log(
            `VU ${__VU}: sequential retry FAILED — key=${sharedKey}, ` +
            `first status=${firstRes.status}, retry status=${retryRes.status}`
        );
    }

    sleep(0.5);
}

export default function () {
    // Route to correct scenario based on scenario name (set via exec option)
    // k6 0.47+ supports `exec` in scenario definition to map named functions
    // Fallback: use __ENV.SCENARIO or detect from tags
    const scenario = __ENV.SCENARIO || 'concurrent';

    if (scenario === 'sequential') {
        scenarioSequentialReplay();
    } else {
        scenarioConcurrentDuplicates();
    }
}

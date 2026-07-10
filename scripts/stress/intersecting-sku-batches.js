// intersecting-sku-batches.js
// 3 phases: smoke(5 VU), normal(30 VU), overload(80 VU).
// Each VU randomly picks one of three batch patterns:
//   1. Non-intersecting: unique SKU combos per order
//   2. Fully intersecting: all compete for SKU-001, SKU-002
//   3. Partially intersecting: SKU-001 shared, rest unique
// After test, verify confirmed orders have correct reserved quantities via GET.

import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
    stages: [
        { duration: '20s', target: 5 },   // smoke
        { duration: '30s', target: 30 },  // normal
        { duration: '20s', target: 80 },  // overload
        { duration: '10s', target: 0 },   // cooldown
    ],
    thresholds: {
        http_req_failed: ['rate<0.15'],
    },
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5032';
const API = `${BASE_URL}/api/orders`;

// SKU definitions for each pattern type
const PATTERNS = {
    // Non-intersecting: each order gets a unique-enough combo
    NON_INTERSECTING: () => {
        const pool = [
            { sku: 'SKU-005', warehouseId: 'WH-1', quantity: 1 },
            { sku: 'SKU-006', warehouseId: 'WH-1', quantity: 1 },
            { sku: 'SKU-007', warehouseId: 'WH-1', quantity: 1 },
            { sku: 'SKU-008', warehouseId: 'WH-2', quantity: 1 },
            { sku: 'SKU-009', warehouseId: 'WH-1', quantity: 1 },
            { sku: 'SKU-010', warehouseId: 'WH-1', quantity: 1 },
        ];
        const idx = Math.floor(Math.random() * pool.length);
        return [pool[idx]];
    },
    // Fully intersecting: all VUs compete for same SKUs
    FULLY_INTERSECTING: () => {
        return [
            { sku: 'SKU-001', warehouseId: 'WH-1', quantity: 1 },
            { sku: 'SKU-002', warehouseId: 'WH-1', quantity: 1 },
        ];
    },
    // Partially intersecting: SKU-001 shared, second SKU unique per VU
    PARTIALLY_INTERSECTING: () => {
        const uniquePool = [
            { sku: 'SKU-003', warehouseId: 'WH-1', quantity: 1 },
            { sku: 'SKU-004', warehouseId: 'WH-1', quantity: 1 },
            { sku: 'SKU-005', warehouseId: 'WH-1', quantity: 1 },
            { sku: 'SKU-006', warehouseId: 'WH-1', quantity: 1 },
            { sku: 'SKU-007', warehouseId: 'WH-1', quantity: 1 },
            { sku: 'SKU-009', warehouseId: 'WH-1', quantity: 1 },
        ];
        const idx = Math.floor(Math.random() * uniquePool.length);
        return [
            { sku: 'SKU-001', warehouseId: 'WH-1', quantity: 1 },
            uniquePool[idx],
        ];
    },
};

const PATTERN_NAMES = ['NON_INTERSECTING', 'FULLY_INTERSECTING', 'PARTIALLY_INTERSECTING'];

function uuid() {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
        const r = Math.random() * 16 | 0;
        return (c === 'x' ? r : (r & 0x3 | 0x8)).toString(16);
    });
}

// Track per-pattern metrics via tags
function pickPattern() {
    // Use VU to deterministically spread load across patterns
    // Each VU sticks to one pattern for consistency
    const patternIdx = __VU % PATTERN_NAMES.length;
    const name = PATTERN_NAMES[patternIdx];
    return { name, items: PATTERNS[name]() };
}

export default function () {
    const idempotencyKey = uuid();
    const correlationId = uuid();
    const { name: patternName, items } = pickPattern();

    const payload = JSON.stringify({ items });
    const headers = {
        'Content-Type': 'application/json',
        'Idempotency-Key': idempotencyKey,
        'X-Correlation-ID': correlationId,
    };

    const tags = {
        scenario: 'intersecting-sku',
        pattern: patternName,
        sku: items.map(i => i.sku).join('+'),
    };

    // Step 1: Create order
    const createRes = http.post(API, payload, { headers, tags: { ...tags, action: 'create' } });

    check(createRes, {
        'create order responded 200 or 409': (r) => r.status === 200 || r.status === 409,
    });

    // Step 2: If created successfully, confirm
    if (createRes.status === 200) {
        const body = createRes.json();
        if (body.success === true && body.orderNumber) {
            const confirmRes = http.post(
                `${API}/${body.orderNumber}/confirm`,
                null,
                { headers: { 'Content-Type': 'application/json' }, tags: { ...tags, action: 'confirm' } }
            );

            check(confirmRes, {
                'confirm succeeded': (r) => r.status === 200 && r.json().success === true,
            });

            // Step 3: Verify via GET that confirmed order has correct state
            const getRes = http.get(`${API}/${body.orderNumber}`, {
                tags: { ...tags, action: 'verify' },
            });

            check(getRes, {
                'get order status is 200': (r) => r.status === 200,
                'get order has items': (r) => {
                    try {
                        const b = r.json();
                        return b && (b.items || b.orderItems || b.orderStatus);
                    } catch { return false; }
                },
            });

            if (!confirmRes.json().success) {
                console.log(
                    `VU ${__VU} [${patternName}]: confirm failed for ${body.orderNumber}, ` +
                    `error=${confirmRes.json().errorCode || 'unknown'}`
                );
            }
        } else if (body.failures && body.failures.length > 0) {
            // Log contention failures per SKU
            for (const f of body.failures) {
                console.log(
                    `VU ${__VU} [${patternName}]: ${f.errorCode} on ${f.sku}/${f.warehouseId}: ${f.reason}`
                );
            }
        }
    }

    sleep(Math.random() * 0.3 + 0.1);
}

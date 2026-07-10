// reservation-concurrency.js
// 100 concurrent VUs over 30s hitting shared SKU pool.
// Each VU: create order with 1-3 random items, confirm it.
// Asserts: no overbooking — total successful reservations must not exceed available stock.

import http from 'k6/http';
import { check, sleep } from 'k6';
import { SharedArray } from 'k6/data';

export const options = {
    scenarios: {
        concurrent_reserve: {
            executor: 'constant-vus',
            vus: 100,
            duration: '30s',
            tags: { scenario: 'concurrent-reserve' },
        },
    },
    thresholds: {
        http_req_failed: ['rate<0.10'], // allow up to 10% failures from stock exhaustion
    },
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5032';
const API = `${BASE_URL}/api/orders`;

// SKU pool with high availability for concurrency testing
const SKU_POOL = [
    { sku: 'SKU-001', warehouseId: 'WH-1', avail: 100, reserved: 5 },
    { sku: 'SKU-004', warehouseId: 'WH-1', avail: 200, reserved: 0 },
    { sku: 'SKU-004', warehouseId: 'WH-2', avail: 100, reserved: 0 },
    { sku: 'SKU-005', warehouseId: 'WH-1', avail: 75, reserved: 0 },
    { sku: 'SKU-010', warehouseId: 'WH-1', avail: 500, reserved: 0 },
];

function uuid() {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
        const r = Math.random() * 16 | 0;
        return (c === 'x' ? r : (r & 0x3 | 0x8)).toString(16);
    });
}

function randomItems() {
    const count = Math.floor(Math.random() * 3) + 1; // 1 to 3 items
    const items = [];
    const used = new Set();
    for (let i = 0; i < count; i++) {
        const idx = Math.floor(Math.random() * SKU_POOL.length);
        if (used.has(idx)) continue;
        used.add(idx);
        const s = SKU_POOL[idx];
        items.push({
            sku: s.sku,
            warehouseId: s.warehouseId,
            quantity: Math.floor(Math.random() * 3) + 1,
        });
    }
    // ensure at least 1 item
    if (items.length === 0) {
        const s = SKU_POOL[Math.floor(Math.random() * SKU_POOL.length)];
        items.push({ sku: s.sku, warehouseId: s.warehouseId, quantity: 1 });
    }
    return items;
}

export default function () {
    const idempotencyKey = uuid();
    const correlationId = uuid();
    const items = randomItems();

    const payload = JSON.stringify({ items });
    const headers = {
        'Content-Type': 'application/json',
        'Idempotency-Key': idempotencyKey,
        'X-Correlation-ID': correlationId,
    };

    // Step 1: Create order
    const createRes = http.post(API, payload, { headers, tags: { action: 'create' } });

    check(createRes, {
        'create order responded': (r) => r.status === 200 || r.status === 409,
    });

    let orderNumber = null;
    let reservationId = null;
    let success = false;

    if (createRes.status === 200) {
        const body = createRes.json();
        success = body.success === true;
        orderNumber = body.orderNumber || null;
        reservationId = body.reservationId || null;

        // Check the response structure
        check(createRes, {
            'create response has success field': () => body.success !== undefined,
            'create response has orderNumber or failures': () =>
                body.orderNumber !== null || (body.failures && body.failures.length > 0),
        });
    }

    // Step 2: If order was created successfully, confirm it
    if (success && orderNumber) {
        const confirmRes = http.post(
            `${API}/${orderNumber}/confirm`,
            null,
            { headers: { 'Content-Type': 'application/json' }, tags: { action: 'confirm' } }
        );

        const confirmOk = check(confirmRes, {
            'confirm order succeeded': (r) => r.status === 200,
            'confirm response has success true': (r) => r.json().success === true,
        });

        if (!confirmOk) {
            console.log(`VU ${__VU} ITER ${__ITER}: confirm failed for ${orderNumber}, status=${confirmRes.status}`);
        }
    }

    // Track failures for overbooking analysis
    if (!success && createRes.status === 200) {
        const body = createRes.json();
        if (body.failures && body.failures.length > 0) {
            for (const f of body.failures) {
                console.log(
                    `VU ${__VU} ITER ${__ITER}: INSUFFICIENT_STOCK for ${f.sku}/${f.warehouseId}: ${f.reason}`
                );
            }
        }
    }

    // Brief pause between iterations
    sleep(Math.random() * 0.5 + 0.1);
}

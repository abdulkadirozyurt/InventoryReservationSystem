// expiry-pressure.js
// Tests inventory recycling through cancel-and-recreate pattern.
// 20 VUs for 60s. Each VU: create order -> cancel order -> recreate order.
// Track: cancel success rate, recreate success rate, inventory recycling.
// Uses SKU-003 (only 3 avail) to demonstrate contention and release.

import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
    scenarios: {
        expiry_pressure: {
            executor: 'ramping-vus',
            startVUs: 0,
            stages: [
                { duration: '10s', target: 20 },   // ramp up
                { duration: '40s', target: 20 },    // steady
                { duration: '10s', target: 0 },     // ramp down
            ],
            tags: { scenario: 'expiry-pressure' },
        },
    },
    thresholds: {
        http_req_failed: ['rate<0.20'],
    },
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5032';
const API = `${BASE_URL}/api/orders`;

// SKU-003 has only 3 available — ideal for testing inventory recycling
const SKU_POOL = [
    { sku: 'SKU-003', warehouseId: 'WH-1', quantity: 1 },
    { sku: 'SKU-006', warehouseId: 'WH-1', quantity: 1 },
    { sku: 'SKU-007', warehouseId: 'WH-1', quantity: 1 },
    { sku: 'SKU-009', warehouseId: 'WH-1', quantity: 1 },
    { sku: 'SKU-010', warehouseId: 'WH-1', quantity: 1 },
];

function uuid() {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
        const r = Math.random() * 16 | 0;
        return (c === 'x' ? r : (r & 0x3 | 0x8)).toString(16);
    });
}

function randomItems() {
    const count = Math.floor(Math.random() * 2) + 1; // 1-2 items
    const items = [];
    const used = new Set();
    for (let i = 0; i < count; i++) {
        const idx = Math.floor(Math.random() * SKU_POOL.length);
        if (used.has(idx)) continue;
        used.add(idx);
        items.push({ ...SKU_POOL[idx] });
    }
    if (items.length === 0) {
        items.push({ ...SKU_POOL[0] });
    }
    return items;
}

export default function () {
    // --- Phase 1: Create order ---
    const items = randomItems();
    const createKey = uuid();
    const createHeaders = {
        'Content-Type': 'application/json',
        'Idempotency-Key': createKey,
        'X-Correlation-ID': uuid(),
    };
    const createPayload = JSON.stringify({ items });
    const tags = { action: 'create' };

    const createRes = http.post(API, createPayload, { headers: createHeaders, tags });

    check(createRes, {
        'create responded': (r) => r.status === 200 || r.status === 409,
    });

    if (createRes.status !== 200) {
        sleep(Math.random() * 0.3 + 0.1);
        return;
    }

    const createBody = createRes.json();
    if (!createBody.success || !createBody.orderNumber) {
        sleep(Math.random() * 0.3 + 0.1);
        return;
    }

    const orderNumber = createBody.orderNumber;
    console.log(`VU ${__VU}: created order ${orderNumber} (items: ${JSON.stringify(items)})`);

    // Brief hold to simulate real usage
    sleep(Math.random() * 1 + 0.5);

    // --- Phase 2: Cancel order to release inventory ---
    const cancelHeaders = {
        'Content-Type': 'application/json',
        'Idempotency-Key': uuid(),
        'X-Correlation-ID': uuid(),
    };
    const cancelPayload = JSON.stringify({ reason: 'expiry-pressure stress test cancellation' });

    const cancelRes = http.post(
        `${API}/${orderNumber}/cancel`,
        cancelPayload,
        { headers: cancelHeaders, tags: { action: 'cancel' } }
    );

    const cancelOk = check(cancelRes, {
        'cancel responded 200': (r) => r.status === 200,
        'cancel success true': (r) => r.json().success === true,
    });

    if (!cancelOk) {
        console.log(
            `VU ${__VU}: cancel failed for ${orderNumber}, ` +
            `status=${cancelRes.status}, body=${cancelRes.body}`
        );
        sleep(Math.random() * 0.3 + 0.1);
        return;
    }

    console.log(`VU ${__VU}: cancelled order ${orderNumber} — inventory should be released`);

    // Brief pause to let inventory release propagate
    sleep(Math.random() * 0.5 + 0.2);

    // --- Phase 3: Recreate order with same SKUs (verify inventory recycling) ---
    const recreateKey = uuid();
    const recreateHeaders = {
        'Content-Type': 'application/json',
        'Idempotency-Key': recreateKey,
        'X-Correlation-ID': uuid(),
    };
    const recreatePayload = JSON.stringify({ items });

    const recreateRes = http.post(API, recreatePayload, {
        headers: recreateHeaders,
        tags: { action: 'recreate' },
    });

    const recreateOk = check(recreateRes, {
        'recreate responded 200': (r) => r.status === 200,
        'recreate success true': (r) => {
            if (r.status !== 200) return false;
            const body = r.json();
            return body.success === true;
        },
    });

    if (recreateOk) {
        console.log(`VU ${__VU}: recreate succeeded after cancel — inventory recycled`);
    } else {
        const body = recreateRes.json();
        const failReasons = (body.failures || [])
            .map(f => `${f.sku}/${f.warehouseId}: ${f.errorCode}`)
            .join(', ');
        console.log(
            `VU ${__VU}: recreate FAILED after cancel — inventory NOT recycled: ${failReasons}`
        );
    }

    sleep(Math.random() * 0.3 + 0.1);
}

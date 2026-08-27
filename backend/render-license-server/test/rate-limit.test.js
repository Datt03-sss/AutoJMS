"use strict";

// ==========================================================================
// The global flood guard, and why it runs before the body parser.
// ==========================================================================
// Every per-route limiter is registered on its route, which puts it AFTER
// express.json in the middleware chain. So an unauthenticated caller could make
// this process allocate and parse a 512 kB JSON body on every request and only
// then be told it was over its limit. On Render's free tier — 512 MB of RAM, one
// instance, no autoscale — the memory was spent before the check that was supposed
// to prevent spending it.
//
// This file lives on its own because the last test deliberately exhausts the
// global budget, and `node --test` gives each FILE its own process: the burn cannot
// leak into another file's requests.
// ==========================================================================

const test = require("node:test");
const assert = require("node:assert/strict");

const { startServer } = require("./helpers/harness");

/** Sent raw, because express.json needs something it cannot parse. */
const postBrokenJson = async baseUrl => {
    const response = await fetch(`${baseUrl}/api/verify-license`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: "{ not json"
    });
    return response.status;
};

test("the default cap is above the sum of every per-route cap", async () => {
    const harness = await startServer();

    try {
        // 60 (verify-license + logout) + 120 (heartbeat) + 30 (health) + 60 (sheets
        // grant) + 60 (datahub assertion) = 330. The default has to clear that sum,
        // or this limiter would quietly become the real policy for a busy office and
        // the per-route numbers would stop meaning anything.
        assert.equal(harness.app.globalRateLimitPerMinute, 600);
        assert.ok(harness.app.globalRateLimitPerMinute > 330);
    } finally {
        await harness.close();
    }
});

test("an operator override is honoured upward", async () => {
    const harness = await startServer({ env: { GLOBAL_RATE_LIMIT_PER_MINUTE: 900 } });

    try {
        assert.equal(harness.app.globalRateLimitPerMinute, 900);
    } finally {
        await harness.close();
    }
});

test("an override below the floor is clamped to the floor", async () => {
    // The floor is heartbeatLimiter's own cap. Below it, this limiter would start
    // refusing heartbeats that the route's own policy allows — and a global 429 also
    // hits /health, which Render polls: an instance answering 429 there is marked
    // unhealthy and restarted, so a low value would take the service down rather
    // than protect it. "0" gets its own case because it reads as "no limit" and
    // means the opposite.
    for (const attempt of ["0", "1", "-5"]) {
        const harness = await startServer({ env: { GLOBAL_RATE_LIMIT_PER_MINUTE: attempt } });
        try {
            assert.equal(harness.app.globalRateLimitPerMinute, 120, `override ${attempt} must clamp to the floor`);
        } finally {
            await harness.close();
        }
    }
});

test("an unparseable override falls back to the default, not to NaN or the floor", async () => {
    // Two wrong answers this rules out. NaN is what `Math.max(120, Number(raw))`
    // returns — and a limiter constructed with max: NaN does not fall back to
    // anything, it behaves undefinedly. The floor is the other wrong answer: a typo
    // in a Render variable should not silently TIGHTEN the flood guard to its
    // minimum, because the operator's intent was never "restrict this".
    // "6 00" rather than "600 ": Number() trims whitespace, so a trailing-space
    // value parses fine and would have passed this test without testing anything.
    for (const attempt of ["not-a-number", "6 00", ""]) {
        const harness = await startServer({ env: { GLOBAL_RATE_LIMIT_PER_MINUTE: attempt } });
        try {
            const resolved = harness.app.globalRateLimitPerMinute;
            assert.ok(Number.isFinite(resolved), `override ${JSON.stringify(attempt)} must not resolve to NaN`);
            assert.equal(resolved, 600, `override ${JSON.stringify(attempt)} must fall back to the default`);
        } finally {
            await harness.close();
        }
    }
});

// Last on purpose: this exhausts the budget for the rest of the process.
test("over the limit, a request is refused before its body is parsed", async () => {
    const harness = await startServer({ env: { GLOBAL_RATE_LIMIT_PER_MINUTE: 120 } });

    try {
        // Under the limit this must be a 400: that is the parser running, and it is
        // what makes the 429 below meaningful rather than a coincidence.
        assert.equal(
            await postBrokenJson(harness.baseUrl),
            400,
            "the body parser must reject this while under the limit"
        );

        // Spend the rest of the window on the cheapest route there is. /health has no
        // per-route limiter, so only the global one can answer 429 here.
        let allowed = 1;
        let refused = 0;
        for (let i = 0; i < 130; i += 1) {
            const response = await harness.get("/health");
            if (response.status === 429) refused += 1;
            else allowed += 1;
        }

        assert.equal(allowed, 120, "the cap is the cap: 120 requests through, then no more");
        assert.ok(refused > 0, "expected the global limiter to start refusing");

        // The same malformed body as above. A 400 here would mean the parser ran
        // first and the limiter was decorative; 429 means the request was turned
        // away before anything was allocated for it.
        assert.equal(
            await postBrokenJson(harness.baseUrl),
            429,
            "an over-limit request must not reach the body parser"
        );
    } finally {
        await harness.close();
    }
});

"use strict";

// ==========================================================================
// The admin API: authentication, key minting, and anchored renewal.
// ==========================================================================
// These routes are the only surface on this process that WRITES to /Licenses,
// so the tests are weighted accordingly: roughly half of them assert that
// nothing was written. A licence dashboard that mints a key from a malformed
// body, or leaves a half-built record behind after a typo'd key, is worse than
// no dashboard — the owner would be reading a fleet that does not exist.
//
// Each test boots its own server, which is what makes the throttling test
// possible: admin-routes builds its rate limiters in its module body, and the
// harness evicts it from require.cache on every boot, so one test's spent
// budget cannot reach the next.
// ==========================================================================

const test = require("node:test");
const assert = require("node:assert/strict");

const { startServer } = require("./helpers/harness");
const { computeExpiry, parseInstant } = require("../license-expiry");

// server.js registers one uncaughtException and one unhandledRejection logger in
// its module body — correct for a process that boots it once, and the reason this
// file trips Node's 10-listener warning after the tenth boot. The handlers only
// log, nothing unregisters them, and a real deployment never reaches two. Raising
// the ceiling keeps a harness artifact out of the suite's output rather than
// silencing a leak.
process.setMaxListeners(64);

/**
 * Not a secret — a fixture, and it never leaves this process. Its length is the
 * point: 27 characters, comfortably over the 16-character floor admin-routes
 * enforces, so the tests below exercise the enabled path rather than the
 * "token too short" one.
 */
const ADMIN_TOKEN = "test-admin-token-0123456789";

const adminEnv = { ADMIN_SECRET_TOKEN: ADMIN_TOKEN };
const auth = (token = ADMIN_TOKEN) => ({ headers: { "x-admin-token": token } });

const withAuth = (options = {}, token = ADMIN_TOKEN) => ({
    ...options,
    headers: { "x-admin-token": token, ...(options.headers || {}) }
});

const DAY_MS = 86_400_000;

/** Splits a stored expiry, and fails loudly if it is not the VN ISO shape. */
function vnParts(iso) {
    const matched = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})\+07:00$/.exec(String(iso));
    assert.ok(matched, `expiry is not an Asia/Ho_Chi_Minh ISO instant: ${iso}`);
    return {
        year: Number(matched[1]),
        month: Number(matched[2]),
        day: Number(matched[3]),
        hour: Number(matched[4]),
        minute: Number(matched[5]),
        second: Number(matched[6])
    };
}

/** Calendar months since year 0, so "one month later" is a subtraction. */
const monthIndex = parts => parts.year * 12 + (parts.month - 1);

/** Asserts the anchor rule itself: every expiry is 00:00 on the 16th. */
function assertAnchored(iso) {
    const parts = vnParts(iso);
    assert.equal(parts.day, 16, `expiry ${iso} is not on the billing anchor day`);
    assert.equal(parts.hour, 0);
    assert.equal(parts.minute, 0);
    assert.equal(parts.second, 0);
    return parts;
}

const licenseSeed = (overrides = {}) => ({
    createdAt: "01-08-2026 09:00",
    status: "active",
    tier: "ULTRA",
    hwid: "0123456789abcdef0123456789abcdef",
    middleCode: "214A03",
    skipHashCheck: true,
    modulePolicy: { autoUpdate: true, silentUpdate: true, applyOnNextStartup: true },
    dataSpreadsheetId: "",
    notes: "",
    ...overrides
});

// ==========================================================================
// AUTHENTICATION
// ==========================================================================

test("with no ADMIN_SECRET_TOKEN configured every route is off, not open", async () => {
    // The harness clears ADMIN_SECRET_TOKEN by default, so this is the shape a
    // fresh Render deploy has before anyone sets the variable.
    const harness = await startServer();

    try {
        const listed = await harness.get("/api/admin/licenses", auth());
        assert.equal(listed.status, 503);
        assert.equal(listed.body.error, "ADMIN_API_DISABLED");

        // Presenting a plausible token changes nothing: there is no configured
        // value to compare it against, so there is nothing to get right.
        const created = await harness.post(
            "/api/admin/licenses/create",
            withAuth({ body: { middleCode: "214A03", tier: "ULTRA", terms: 1 } })
        );
        assert.equal(created.status, 503);
        assert.equal(created.body.error, "ADMIN_API_DISABLED");

        assert.deepEqual(harness.db.writes(), [], "a disabled admin API must not write");
    } finally {
        await harness.close();
    }
});

test("a token shorter than the floor is treated as no token at all", async () => {
    // The failure this prevents is silent: "admin" in the env would look
    // configured in the Render dashboard while protecting nothing.
    const harness = await startServer({ env: { ADMIN_SECRET_TOKEN: "admin" } });

    try {
        const response = await harness.get("/api/admin/licenses", auth("admin"));
        assert.equal(response.status, 503);
        assert.equal(response.body.error, "ADMIN_API_DISABLED");

        const adminRoutes = require("../admin-routes");
        assert.equal(adminRoutes.adminApiEnabled, false);
        assert.match(adminRoutes.adminDisabledReason, /shorter than 16 characters/);
        // The reason is what reaches Render's log viewer, so it must name the
        // variable and never carry the value that was rejected.
        assert.doesNotMatch(adminRoutes.adminDisabledReason, /admin"|'admin'/);
    } finally {
        await harness.close();
    }
});

test("a missing or wrong X-Admin-Token is rejected, the right one is accepted", async () => {
    const harness = await startServer({ env: adminEnv, seed: { Licenses: {} } });

    try {
        const noHeader = await harness.get("/api/admin/licenses");
        assert.equal(noHeader.status, 401);
        assert.equal(noHeader.body.error, "ADMIN_UNAUTHORIZED");

        const wrong = await harness.get("/api/admin/licenses", auth("test-admin-token-0123456788"));
        assert.equal(wrong.status, 401);

        // A prefix of the real token must not pass: tokensMatch hashes both
        // sides, so length is not part of the comparison an attacker can probe.
        const prefix = await harness.get("/api/admin/licenses", auth(ADMIN_TOKEN.slice(0, 20)));
        assert.equal(prefix.status, 401);

        const accepted = await harness.get("/api/admin/licenses", auth());
        assert.equal(accepted.status, 200);
        assert.equal(accepted.body.success, true);
    } finally {
        await harness.close();
    }
});

test("wrong tokens are throttled, and correct ones do not spend the budget", async () => {
    const harness = await startServer({ env: adminEnv, seed: { Licenses: {} } });

    try {
        // Ten good requests first. If these counted against the failure budget
        // the guesser's ten attempts would already be gone — and so would the
        // owner's, which is the bug requestWasSuccessful() is there to avoid.
        for (let i = 0; i < 10; i += 1) {
            const ok = await harness.get("/api/admin/licenses", auth());
            assert.equal(ok.status, 200);
        }

        for (let attempt = 1; attempt <= 10; attempt += 1) {
            const rejected = await harness.get("/api/admin/licenses", auth("wrong-token-but-long-enough"));
            assert.equal(rejected.status, 401, `attempt ${attempt} should still be an auth failure`);
        }

        const throttled = await harness.get("/api/admin/licenses", auth("wrong-token-but-long-enough"));
        assert.equal(throttled.status, 429);
        assert.equal(throttled.body.error, "ADMIN_AUTH_THROTTLED");

        // The lockout is per IP, not per token, so the correct token is refused
        // too until the window rolls. That is the intended trade: the owner has
        // one browser tab, an online guesser has a loop.
        const alsoThrottled = await harness.get("/api/admin/licenses", auth());
        assert.equal(alsoThrottled.status, 429);
    } finally {
        await harness.close();
    }
});

// ==========================================================================
// CREATE
// ==========================================================================

test("a created key has the XXXX-middlecode-XXXX shape and the fleet's record fields", async () => {
    const harness = await startServer({ env: adminEnv, seed: { Licenses: {} } });

    try {
        const response = await harness.post(
            "/api/admin/licenses/create",
            // Lowercase on purpose: the owner types a post-office code, not a
            // Firebase path, and a lowercase middle group would produce a key
            // that does not match the fleet's shape.
            withAuth({ body: { middleCode: "214a03", tier: "ultra", terms: 1, notes: " khách mới " } })
        );

        assert.equal(response.status, 201);
        assert.match(response.body.key, /^[A-Z0-9]{4}-214A03-[A-Z0-9]{4}$/);

        const stored = harness.db.read(`Licenses/${response.body.key}`);
        assert.ok(stored, "the key in the response must exist in Firebase");
        assert.equal(stored.status, "active");
        assert.equal(stored.tier, "ULTRA");
        assert.equal(stored.middleCode, "214A03");
        assert.equal(stored.hwid, "");
        assert.equal(stored.dataSpreadsheetId, "");
        assert.equal(stored.skipHashCheck, true);
        assert.equal(stored.notes, "khách mới");
        assert.deepEqual(stored.modulePolicy, {
            autoUpdate: true,
            silentUpdate: true,
            applyOnNextStartup: true
        });
        // "DD-MM-YYYY HH:mm" — the shape every hand-written record in the fleet
        // already carries, and the one parseInstant() reads back as VN local time.
        assert.match(stored.createdAt, /^\d{2}-\d{2}-\d{4} \d{2}:\d{2}$/);

        // Two keys in a row must not collide, which is the only thing the
        // random groups are for.
        const second = await harness.post(
            "/api/admin/licenses/create",
            withAuth({ body: { middleCode: "214A03", tier: "BASE", terms: 1 } })
        );
        assert.equal(second.status, 201);
        assert.notEqual(second.body.key, response.body.key);
    } finally {
        await harness.close();
    }
});

test("a new monthly key expires at 00:00 on the 16th, at least 30 days out", async () => {
    const harness = await startServer({ env: adminEnv, seed: { Licenses: {} } });

    try {
        const createdAt = Date.now();
        const response = await harness.post(
            "/api/admin/licenses/create",
            withAuth({ body: { middleCode: "HN01", tier: "ULTRA", terms: 1 } })
        );

        assert.equal(response.status, 201);
        assertAnchored(response.body.license.expiresAt);

        // The business rule, asserted directly rather than by re-running
        // computeExpiry: at least a 30-day term, plus the leftover days needed
        // to reach the anchor — which can never exceed one more month.
        const expiresAtMs = parseInstant(response.body.license.expiresAt);
        const days = (expiresAtMs - createdAt) / DAY_MS;
        assert.ok(days >= 29.5, `term is only ${days.toFixed(1)} days`);
        assert.ok(days <= 62, `term stretched to ${days.toFixed(1)} days`);

        assert.ok(response.body.license.daysRemaining >= 30);
        assert.ok(response.body.license.daysRemaining <= 62);

        const stored = harness.db.read(`Licenses/${response.body.key}`);
        assert.equal(stored.expiresAt, response.body.license.expiresAt);

        // Three terms is still one expiry on an anchor day, two months further out.
        const quarterly = await harness.post(
            "/api/admin/licenses/create",
            withAuth({ body: { middleCode: "HN01", tier: "ULTRA", terms: 3 } })
        );
        assert.equal(quarterly.status, 201);
        const quarterlyParts = assertAnchored(quarterly.body.license.expiresAt);
        assert.equal(monthIndex(quarterlyParts) - monthIndex(vnParts(response.body.license.expiresAt)), 2);
    } finally {
        await harness.close();
    }
});

test("an omitted term mints a perpetual key with no expiry", async () => {
    const harness = await startServer({ env: adminEnv, seed: { Licenses: {} } });

    try {
        const response = await harness.post(
            "/api/admin/licenses/create",
            withAuth({ body: { middleCode: "HN01", tier: "BASE" } })
        );

        assert.equal(response.status, 201);
        assert.equal(response.body.license.expiresAt, null);
        assert.equal(response.body.license.daysRemaining, null);

        // Realtime Database drops a null child, so the stored record is the v1
        // shape evaluateLicense() treats as never expiring. The in-memory double
        // keeps the property, so both readings are accepted here — what matters
        // is that parseInstant finds no instant.
        const stored = harness.db.read(`Licenses/${response.body.key}`);
        assert.equal(parseInstant(stored.expiresAt), null);
    } finally {
        await harness.close();
    }
});

test("a malformed create body is refused and writes nothing", async () => {
    const harness = await startServer({ env: adminEnv, seed: { Licenses: {} } });

    try {
        const cases = [
            [{ middleCode: "214A03", tier: "PRO", terms: 1 }, "INVALID_TIER"],
            [{ middleCode: "214A03", terms: 1 }, "INVALID_TIER"],
            [{ middleCode: "21/4A03", tier: "ULTRA", terms: 1 }, "INVALID_MIDDLE_CODE"],
            [{ middleCode: "", tier: "ULTRA", terms: 1 }, "INVALID_MIDDLE_CODE"],
            // Refused at issue time because /api/verify-license refuses it in the
            // field; the owner should learn now, not when the station fails to enrol.
            [{ middleCode: "000000", tier: "ULTRA", terms: 1 }, "PLACEHOLDER_MIDDLE_CODE"],
            [{ middleCode: "214A03", tier: "ULTRA", terms: 999 }, "INVALID_TERMS"],
            [{ middleCode: "214A03", tier: "ULTRA", terms: 1.5 }, "INVALID_TERMS"],
            // An array stringifies to "", which would otherwise read as
            // "perpetual" and sell a lifetime licence by accident.
            [{ middleCode: "214A03", tier: "ULTRA", terms: [] }, "INVALID_TERMS"]
        ];

        for (const [body, expectedError] of cases) {
            const response = await harness.post("/api/admin/licenses/create", withAuth({ body }));
            assert.equal(response.status, 400, `${JSON.stringify(body)} should be refused`);
            assert.equal(response.body.error, expectedError, `${JSON.stringify(body)}`);
        }

        assert.deepEqual(
            harness.db.writes(),
            [],
            "a refused create must not leave a record behind"
        );
    } finally {
        await harness.close();
    }
});

test("a key the dashboard previewed is written verbatim instead of being re-rolled", async () => {
    const harness = await startServer({ env: adminEnv, seed: { Licenses: {} } });

    try {
        // The whole point of the General Key button: the string the owner read
        // in the modal is the string that ends up in Firebase. A server that
        // quietly minted a different one would leave the owner pasting a key
        // that does not exist.
        const response = await harness.post(
            "/api/admin/licenses/create",
            withAuth({ body: { key: "JMXS-214A03-22B1", middleCode: "214A03", tier: "ULTRA", terms: 1 } })
        );

        assert.equal(response.status, 201);
        assert.equal(response.body.key, "JMXS-214A03-22B1");
        assert.ok(harness.db.read("Licenses/JMXS-214A03-22B1"), "the previewed key must be the one written");

        // Lowercase in, uppercase out — the owner types a post-office code, and
        // a lowercase key would not match the shape the rest of the fleet has.
        const lowercased = await harness.post(
            "/api/admin/licenses/create",
            withAuth({ body: { key: "abcd-214a03-ef12", middleCode: "214a03", tier: "BASE", terms: 1 } })
        );
        assert.equal(lowercased.status, 201);
        assert.equal(lowercased.body.key, "ABCD-214A03-EF12");

        // customKey is the documented alias. Spelling it the other way must
        // honour the key, not silently fall back to a random one.
        const aliased = await harness.post(
            "/api/admin/licenses/create",
            withAuth({ body: { customKey: "ZZZZ-214A03-9999", middleCode: "214A03", tier: "BASE", terms: 1 } })
        );
        assert.equal(aliased.status, 201);
        assert.equal(aliased.body.key, "ZZZZ-214A03-9999");
    } finally {
        await harness.close();
    }
});

test("a client key that is not XXXX-middleCode-XXXX is refused and writes nothing", async () => {
    const harness = await startServer({ env: adminEnv, seed: { Licenses: {} } });

    try {
        const cases = [
            // Three characters in the first group.
            "JMX-214A03-22B1",
            // A middle group that is not the middle code just validated — the
            // one that would produce a key the site code cannot be read off.
            "JMXS-HN01-22B1",
            // A character outside [A-Z0-9].
            "JMXS-214A03-22B!",
            // Missing a group entirely.
            "JMXS-214A03",
            // A slash would walk to a different Firebase node.
            "JMXS-214A03-22/1"
        ];

        for (const key of cases) {
            const response = await harness.post(
                "/api/admin/licenses/create",
                withAuth({ body: { key, middleCode: "214A03", tier: "ULTRA", terms: 1 } })
            );
            assert.equal(response.status, 400, `${key} should be refused`);
            assert.equal(response.body.error, "INVALID_LICENSE_KEY", key);
            // Refused, not corrected: a server that rolled a random key here
            // would answer 201 with a key the owner never saw.
            assert.ok(!("key" in response.body), `${key} must not come back with a substitute key`);
        }

        assert.deepEqual(harness.db.writes(), [], "a refused key must not leave a record behind");
    } finally {
        await harness.close();
    }
});

test("a client key that is already taken is refused rather than overwriting the record", async () => {
    const harness = await startServer({
        env: adminEnv,
        seed: { Licenses: { "JMXS-214A03-22B1": licenseSeed({ notes: "khách cũ" }) } }
    });

    try {
        const response = await harness.post(
            "/api/admin/licenses/create",
            withAuth({ body: { key: "JMXS-214A03-22B1", middleCode: "214A03", tier: "BASE", terms: 1 } })
        );

        // 409, because the write is a set(): honouring this would have replaced
        // a paying customer's record with a blank one.
        assert.equal(response.status, 409);
        assert.equal(response.body.error, "LICENSE_KEY_TAKEN");

        const stored = harness.db.read("Licenses/JMXS-214A03-22B1");
        assert.equal(stored.notes, "khách cũ", "the live record must be untouched");
        assert.equal(stored.tier, "ULTRA");
        assert.deepEqual(harness.db.writes(), []);
    } finally {
        await harness.close();
    }
});

test("dataSpreadsheetId is stored for ULTRA and blanked for BASE", async () => {
    const harness = await startServer({ env: adminEnv, seed: { Licenses: {} } });

    try {
        const ultra = await harness.post(
            "/api/admin/licenses/create",
            withAuth({
                body: {
                    middleCode: "214A03",
                    tier: "ULTRA",
                    terms: 1,
                    dataSpreadsheetId: "  1AbCdEfGhIjKlMnOpQrStUvWxYz  "
                }
            })
        );
        assert.equal(ultra.status, 201);
        assert.equal(harness.db.read(`Licenses/${ultra.body.key}`).dataSpreadsheetId, "1AbCdEfGhIjKlMnOpQrStUvWxYz");

        // BASE has no sheet in the client, so a value sent for one is dropped
        // rather than stored: a record that looks provisioned but is not is
        // worse than an empty one.
        const base = await harness.post(
            "/api/admin/licenses/create",
            withAuth({
                body: { middleCode: "214A03", tier: "BASE", terms: 1, dataSpreadsheetId: "1AbCdEfGhIjKlMnOpQrStUvWxYz" }
            })
        );
        assert.equal(base.status, 201);
        assert.equal(harness.db.read(`Licenses/${base.body.key}`).dataSpreadsheetId, "");

        // Anything that is not text becomes "", never "[object Object]".
        const junk = await harness.post(
            "/api/admin/licenses/create",
            withAuth({ body: { middleCode: "214A03", tier: "ULTRA", terms: 1, dataSpreadsheetId: { id: 7 } } })
        );
        assert.equal(junk.status, 201);
        assert.equal(harness.db.read(`Licenses/${junk.body.key}`).dataSpreadsheetId, "");

        // Bounded, so a stray paste cannot write a novel into the record.
        const huge = await harness.post(
            "/api/admin/licenses/create",
            withAuth({ body: { middleCode: "214A03", tier: "ULTRA", terms: 1, dataSpreadsheetId: "A".repeat(5000) } })
        );
        assert.equal(huge.status, 201);
        assert.equal(harness.db.read(`Licenses/${huge.body.key}`).dataSpreadsheetId.length, 200);
    } finally {
        await harness.close();
    }
});

test("module switches default to on and only an explicit false turns one off", async () => {
    const harness = await startServer({ env: adminEnv, seed: { Licenses: {} } });

    try {
        // No modulePolicy at all is the shape every hand-written record in the
        // fleet has: all three on. Defaulting these to off would ship a key
        // that never updates itself and looks like a broken build.
        const omitted = await harness.post(
            "/api/admin/licenses/create",
            withAuth({ body: { middleCode: "214A03", tier: "ULTRA", terms: 1 } })
        );
        assert.equal(omitted.status, 201);
        assert.deepEqual(harness.db.read(`Licenses/${omitted.body.key}`).modulePolicy, {
            autoUpdate: true,
            silentUpdate: true,
            applyOnNextStartup: true
        });

        const partial = await harness.post(
            "/api/admin/licenses/create",
            withAuth({
                body: {
                    middleCode: "214A03",
                    tier: "ULTRA",
                    terms: 1,
                    skipHashCheck: false,
                    modulePolicy: { silentUpdate: false }
                }
            })
        );
        assert.equal(partial.status, 201);

        const stored = harness.db.read(`Licenses/${partial.body.key}`);
        assert.equal(stored.skipHashCheck, false);
        // The two switches the body never mentioned stay on.
        assert.deepEqual(stored.modulePolicy, {
            autoUpdate: true,
            silentUpdate: false,
            applyOnNextStartup: true
        });
    } finally {
        await harness.close();
    }
});

// ==========================================================================
// EXTEND
// ==========================================================================

test("one term moves a live expiry exactly one anchored month forward", async () => {
    // Seeded from computeExpiry rather than a hard-coded date so the test does
    // not start failing once the calendar passes it.
    const current = computeExpiry(Date.now(), { terms: 1 });
    const key = "JMXS-214A03-22B1";
    const harness = await startServer({
        env: adminEnv,
        seed: { Licenses: { [key]: licenseSeed({ expiresAt: current.expiresAt }) } }
    });

    try {
        const response = await harness.post(
            `/api/admin/licenses/${key}/extend`,
            withAuth({ body: { terms: 1 } })
        );

        assert.equal(response.status, 200);
        assert.equal(response.body.previousExpiresAt, current.expiresAt);

        const before = assertAnchored(current.expiresAt);
        const after = assertAnchored(response.body.expiresAt);
        assert.equal(monthIndex(after) - monthIndex(before), 1);

        assert.equal(harness.db.read(`Licenses/${key}`).expiresAt, response.body.expiresAt);
        // Nothing else may move: an extension is an expiry change, not a re-issue.
        assert.equal(harness.db.read(`Licenses/${key}`).status, "active");
        assert.equal(harness.db.read(`Licenses/${key}`).tier, "ULTRA");
        assert.equal(harness.db.read(`Licenses/${key}`).createdAt, "01-08-2026 09:00");
    } finally {
        await harness.close();
    }
});

test("twelve terms move a live expiry exactly one year forward", async () => {
    const current = computeExpiry(Date.now(), { terms: 1 });
    const key = "JMXS-214A03-22B1";
    const harness = await startServer({
        env: adminEnv,
        seed: { Licenses: { [key]: licenseSeed({ expiresAt: current.expiresAt }) } }
    });

    try {
        const response = await harness.post(
            `/api/admin/licenses/${key}/extend`,
            withAuth({ body: { terms: 12 } })
        );

        assert.equal(response.status, 200);

        const before = vnParts(current.expiresAt);
        const after = assertAnchored(response.body.expiresAt);
        assert.equal(monthIndex(after) - monthIndex(before), 12);
        assert.equal(after.year - before.year, 1);
        assert.equal(after.month, before.month);
    } finally {
        await harness.close();
    }
});

test("a long-lapsed key restarts its term from today rather than from its old expiry", async () => {
    const key = "JMXS-214A03-22B1";
    const harness = await startServer({
        env: adminEnv,
        seed: { Licenses: { [key]: licenseSeed({ expiresAt: "2020-01-16T00:00:00+07:00" }) } }
    });

    try {
        const response = await harness.post(
            `/api/admin/licenses/${key}/extend`,
            withAuth({ body: { terms: 1 } })
        );

        assert.equal(response.status, 200);
        assert.equal(response.body.previousExpiresAt, "2020-01-16T00:00:00+07:00");

        assertAnchored(response.body.expiresAt);
        // Continuing from 2020 would have produced 2020-02-16 — a renewal the
        // customer paid for that had already expired.
        const expiresAtMs = parseInstant(response.body.expiresAt);
        assert.ok(expiresAtMs > Date.now(), "a renewal must not land in the past");
        assert.ok((expiresAtMs - Date.now()) / DAY_MS >= 29.5);
        assert.ok(response.body.daysRemaining >= 30);
    } finally {
        await harness.close();
    }
});

test("a perpetual key is refused for extension instead of being given an expiry", async () => {
    const key = "JMXS-214A03-22B1";
    const harness = await startServer({
        env: adminEnv,
        seed: { Licenses: { [key]: licenseSeed() } }
    });

    try {
        const response = await harness.post(
            `/api/admin/licenses/${key}/extend`,
            withAuth({ body: { terms: 1 } })
        );

        assert.equal(response.status, 409);
        assert.equal(response.body.error, "LICENSE_IS_PERPETUAL");
        assert.deepEqual(harness.db.writes(), [], "a perpetual key must keep having no expiry");
    } finally {
        await harness.close();
    }
});

test("extend refuses a perpetual term, so a renewal cannot become a lifetime key", async () => {
    const current = computeExpiry(Date.now(), { terms: 1 });
    const key = "JMXS-214A03-22B1";
    const harness = await startServer({
        env: adminEnv,
        seed: { Licenses: { [key]: licenseSeed({ expiresAt: current.expiresAt }) } }
    });

    try {
        for (const terms of [0, "", null, "perpetual"]) {
            const response = await harness.post(
                `/api/admin/licenses/${key}/extend`,
                withAuth({ body: { terms } })
            );
            assert.equal(response.status, 400, `terms ${JSON.stringify(terms)} should be refused`);
            assert.equal(response.body.error, "INVALID_TERMS");
        }

        assert.deepEqual(harness.db.writes(), []);
    } finally {
        await harness.close();
    }
});

// ==========================================================================
// STATUS, HWID, TIER
// ==========================================================================

test("toggle-status flips between active and revoked", async () => {
    const key = "JMXS-214A03-22B1";
    const harness = await startServer({
        env: adminEnv,
        seed: { Licenses: { [key]: licenseSeed() } }
    });

    try {
        const revoke = await harness.post(`/api/admin/licenses/${key}/toggle-status`, withAuth());
        assert.equal(revoke.status, 200);
        assert.equal(revoke.body.status, "revoked");
        assert.equal(harness.db.read(`Licenses/${key}`).status, "revoked");

        const restore = await harness.post(`/api/admin/licenses/${key}/toggle-status`, withAuth());
        assert.equal(restore.status, 200);
        assert.equal(restore.body.status, "active");
        assert.equal(harness.db.read(`Licenses/${key}`).status, "active");
    } finally {
        await harness.close();
    }
});

test("a record in any other state opens rather than toggling deeper", async () => {
    // "suspended" is already refused by verify-license, so the only move that
    // helps the customer on the phone is to open it.
    const key = "JMXS-214A03-22B1";
    const harness = await startServer({
        env: adminEnv,
        seed: { Licenses: { [key]: licenseSeed({ status: "suspended" }) } }
    });

    try {
        const response = await harness.post(`/api/admin/licenses/${key}/toggle-status`, withAuth());
        assert.equal(response.status, 200);
        assert.equal(response.body.status, "active");
    } finally {
        await harness.close();
    }
});

test("unbind-hwid clears the binding and leaves everything else alone", async () => {
    const key = "JMXS-214A03-22B1";
    const harness = await startServer({
        env: adminEnv,
        seed: { Licenses: { [key]: licenseSeed() } }
    });

    try {
        const response = await harness.post(`/api/admin/licenses/${key}/unbind-hwid`, withAuth());

        assert.equal(response.status, 200);
        assert.equal(response.body.hwid, "");

        const stored = harness.db.read(`Licenses/${key}`);
        assert.equal(stored.hwid, "");
        assert.equal(stored.status, "active");
        assert.equal(stored.tier, "ULTRA");
        assert.equal(stored.middleCode, "214A03");
    } finally {
        await harness.close();
    }
});

test("a tier change is re-validated on the server, not trusted from the UI", async () => {
    const key = "JMXS-214A03-22B1";
    const harness = await startServer({
        env: adminEnv,
        seed: { Licenses: { [key]: licenseSeed() } }
    });

    try {
        const downgraded = await harness.post(
            `/api/admin/licenses/${key}/tier`,
            withAuth({ body: { tier: "base" } })
        );
        assert.equal(downgraded.status, 200);
        assert.equal(downgraded.body.tier, "BASE");
        assert.equal(harness.db.read(`Licenses/${key}`).tier, "BASE");

        // A tier server.js does not recognise reads to the customer as a dead
        // key, so it must not be storable.
        const invalid = await harness.post(
            `/api/admin/licenses/${key}/tier`,
            withAuth({ body: { tier: "GOLD" } })
        );
        assert.equal(invalid.status, 400);
        assert.equal(invalid.body.error, "INVALID_TIER");
        assert.equal(harness.db.read(`Licenses/${key}`).tier, "BASE");
    } finally {
        await harness.close();
    }
});

test("a mutation on an unknown key answers 404 and creates no ghost record", async () => {
    // The guard that matters most here: ref().update() on a path that does not
    // exist CREATES it. Without the existence check, one mistyped key in the URL
    // would leave a record holding nothing but { status: "revoked" } — a licence
    // with no tier, no middle code and no owner.
    const missing = "JMXS-999999-ZZZZ";
    const harness = await startServer({ env: adminEnv, seed: { Licenses: {} } });

    try {
        const routes = [
            [`/api/admin/licenses/${missing}/extend`, { terms: 1 }],
            [`/api/admin/licenses/${missing}/toggle-status`, undefined],
            [`/api/admin/licenses/${missing}/unbind-hwid`, undefined],
            [`/api/admin/licenses/${missing}/tier`, { tier: "ULTRA" }]
        ];

        for (const [route, body] of routes) {
            const response = await harness.post(route, withAuth({ body }));
            assert.equal(response.status, 404, `${route} should be a 404`);
            assert.equal(response.body.error, "LICENSE_NOT_FOUND");
        }

        assert.equal(harness.db.read(`Licenses/${missing}`), null);
        assert.deepEqual(harness.db.writes(), []);
    } finally {
        await harness.close();
    }
});

test("a key that would break the Firebase path is refused before ref() sees it", async () => {
    const harness = await startServer({ env: adminEnv, seed: { Licenses: {} } });

    try {
        // A dot is illegal in a Realtime Database key name; ref() throws on it
        // and the throw carries the database URL, so it must never get that far.
        const response = await harness.post(
            "/api/admin/licenses/bad.key/toggle-status",
            withAuth()
        );

        assert.equal(response.status, 400);
        assert.equal(response.body.error, "INVALID_LICENSE_KEY");
        assert.deepEqual(harness.db.reads(), [], "an illegal key must not reach Firebase");
    } finally {
        await harness.close();
    }
});

// ==========================================================================
// LIST
// ==========================================================================

test("the list evaluates each record and puts the soonest expiry first", async () => {
    const soon = new Date(Date.now() + 3 * DAY_MS).toISOString();
    const later = new Date(Date.now() + 40 * DAY_MS).toISOString();
    const lapsed = new Date(Date.now() - 30 * DAY_MS).toISOString();

    const harness = await startServer({
        env: adminEnv,
        seed: {
            Licenses: {
                "AAAA-HN01-0002": licenseSeed({ middleCode: "HN01", expiresAt: later, tier: "BASE" }),
                "AAAA-HN01-0001": licenseSeed({ middleCode: "HN01", expiresAt: soon }),
                "AAAA-SG02-0003": licenseSeed({ middleCode: "SG02", expiresAt: lapsed }),
                "AAAA-SG02-0004": licenseSeed({
                    middleCode: "SG02",
                    status: "revoked",
                    expiresAt: later,
                    notes: "khách nợ tiền"
                }),
                "AAAA-DN03-0005": licenseSeed({ middleCode: "DN03" })
            }
        }
    });

    try {
        const response = await harness.get("/api/admin/licenses", auth());

        assert.equal(response.status, 200);
        assert.equal(response.body.count, 5);

        const byKey = Object.fromEntries(response.body.licenses.map(item => [item.key, item]));

        assert.equal(byKey["AAAA-HN01-0001"].effectiveStatus, "active");
        assert.equal(byKey["AAAA-HN01-0001"].daysRemaining, 3);
        assert.equal(byKey["AAAA-HN01-0002"].tier, "BASE");

        // 30 days past expiry is well beyond the 7-day grace window.
        assert.equal(byKey["AAAA-SG02-0003"].effectiveStatus, "expired");

        // A revoked key still shows the date it was sold through. evaluateLicense
        // returns null for it, and passing that through would blank the expiry
        // column for exactly the keys the owner is most likely to be inspecting.
        assert.equal(byKey["AAAA-SG02-0004"].effectiveStatus, "revoked");
        assert.equal(byKey["AAAA-SG02-0004"].status, "revoked");
        assert.ok(byKey["AAAA-SG02-0004"].expiresAt, "a revoked key keeps its stored expiry");
        assert.equal(byKey["AAAA-SG02-0004"].notes, "khách nợ tiền");

        assert.equal(byKey["AAAA-DN03-0005"].expiresAt, null);
        assert.equal(byKey["AAAA-DN03-0005"].daysRemaining, null);
        assert.equal(byKey["AAAA-DN03-0005"].effectiveStatus, "active");

        // Soonest first — including the already-expired one, which is the most
        // urgent of all. Perpetual and revoked keys (no daysRemaining) sort last.
        const order = response.body.licenses.map(item => item.key);
        assert.deepEqual(order.slice(0, 3), ["AAAA-SG02-0003", "AAAA-HN01-0001", "AAAA-HN01-0002"]);
        assert.deepEqual(order.slice(3).sort(), ["AAAA-DN03-0005", "AAAA-SG02-0004"]);
    } finally {
        await harness.close();
    }
});

test("an empty or absent Licenses node lists nothing instead of failing", async () => {
    const harness = await startServer({ env: adminEnv, seed: {} });

    try {
        const response = await harness.get("/api/admin/licenses", auth());

        assert.equal(response.status, 200);
        assert.equal(response.body.count, 0);
        assert.deepEqual(response.body.licenses, []);
    } finally {
        await harness.close();
    }
});

// ==========================================================================
// TRANSPORT
// ==========================================================================

test("the dashboard is served from /admin and kept out of search indexes", async () => {
    const harness = await startServer({ env: adminEnv });

    try {
        const page = await harness.get("/admin/index.html");

        assert.equal(page.status, 200);
        assert.equal(page.headers.get("x-robots-tag"), "noindex, nofollow");
        // Loaded as a separate file, not inlined: helmet's default CSP is
        // script-src 'self', which blocks an inline <script> outright.
        assert.match(page.text, /<script src="app\.js"/);

        const script = await harness.get("/admin/app.js");
        assert.equal(script.status, 200);
    } finally {
        await harness.close();
    }
});

test("a Firebase outage answers 503 rather than hanging the request", async () => {
    const harness = await startServer({ env: adminEnv, seed: { Licenses: {} } });

    try {
        harness.db.failNextWith("FIREBASE_TIMEOUT");

        const response = await harness.get("/api/admin/licenses", auth());

        assert.equal(response.status, 503);
        assert.equal(response.body.error, "FIREBASE_TIMEOUT");
        // The message reaching the browser must not carry the database URL or
        // the project id that Firebase puts in its own error text.
        assert.doesNotMatch(response.body.message, /firebaseio|autojms-test/);
    } finally {
        await harness.close();
    }
});

"use strict";

// The health surface is the only part of this server an anonymous caller may
// reach, which makes it the only part where a leaked error string or an unmetered
// database read is reachable by anyone on the internet. Both are asserted here.
//
// /health/firebase/licenses exists because /health/firebase only proves a socket:
// the failure this server cannot survive is "the service account may no longer
// READ /Licenses", which is what a rules change causes and what a socket probe
// reports as healthy.

const test = require("node:test");
const assert = require("node:assert/strict");

const { FIXTURE, startServer, activeLicense } = require("./helpers/harness");

let harness;

test.before(async () => {
    harness = await startServer({ seed: { Licenses: { [FIXTURE.licenseKey]: activeLicense() } } });
});

test.after(async () => {
    await harness.close();
});

test("/health answers without touching the database", async () => {
    harness.db.reset({});

    const response = await harness.get("/health");

    // Render polls this every few seconds; a database read here would multiply
    // Firebase cost by the platform's own health interval.
    assert.equal(response.status, 200);
    assert.equal(response.body.ok, true);
    assert.equal(response.body.service, "autojms-license-server");
    assert.deepEqual(harness.db.reads(), []);
});

test("/health/firebase probes the connection", async () => {
    harness.db.reset({});

    const response = await harness.get("/health/firebase");

    assert.equal(response.status, 200);
    assert.equal(response.body.ok, true);
    assert.equal(response.body.service, "firebase");
    assert.deepEqual(harness.db.reads(), [".info/connected"]);
});

test("/health/firebase/licenses proves the account can read the node", async () => {
    harness.db.reset({ Licenses: { [FIXTURE.licenseKey]: activeLicense() } });

    const response = await harness.get("/health/firebase/licenses");

    assert.equal(response.status, 200);
    assert.equal(response.body.readable, true);
    assert.equal(response.body.hasAny, true);
    // limitToFirst(1): a poll costs one small node, not the whole fleet.
    assert.deepEqual(harness.db.reads(), ["Licenses#limitToFirst(1)"]);
});

test("/health/firebase/licenses never returns license content", async () => {
    harness.db.reset({ Licenses: { [FIXTURE.licenseKey]: activeLicense() } });

    const response = await harness.get("/health/firebase/licenses");

    // An anonymous caller learns "readable" and nothing else — not a key, not a
    // count, not a tier.
    assert.deepEqual(Object.keys(response.body).sort(), [
        "elapsedMs",
        "hasAny",
        "ok",
        "readable",
        "service"
    ]);
    assert.ok(!response.text.includes(FIXTURE.licenseKey));
});

test("an empty /Licenses node is reported as readable but empty", async () => {
    harness.db.reset({});

    const response = await harness.get("/health/firebase/licenses");

    // Worth distinguishing from "cannot read": an empty node on a live deployment
    // means the wrong FIREBASE_DATABASE_URL, and the server otherwise looks fine
    // while every verify-license returns LICENSE_NOT_FOUND.
    assert.equal(response.status, 200);
    assert.equal(response.body.readable, true);
    assert.equal(response.body.hasAny, false);
});

test("a failed probe reports a code, not the underlying error", async () => {
    harness.db.reset({});
    harness.db.failNextWith(
        "FIREBASE FATAL ERROR: Can't determine Firebase Database URL for https://autojms-test.firebaseio.test/ using svc@autojms-test.iam.gserviceaccount.com"
    );

    const response = await harness.get("/health/firebase");

    // A Firebase error string carries the database URL, the project id, and
    // sometimes the service account email. It used to be echoed verbatim to an
    // anonymous caller; an operator reads it in the Render log instead.
    assert.equal(response.status, 503);
    assert.equal(response.body.error, "FIREBASE_UNAVAILABLE");
    assert.ok(!response.text.includes("firebaseio.test"));
    assert.ok(!response.text.includes("gserviceaccount.com"));
});

test("a timeout is distinguished from an unavailable database", async () => {
    harness.db.reset({});
    harness.db.failNextWith("FIREBASE_HEALTH_LICENSES_READ_TIMEOUT");

    const response = await harness.get("/health/firebase/licenses");

    assert.equal(response.status, 503);
    assert.equal(response.body.error, "FIREBASE_TIMEOUT");
});

test("the Firebase probes are rate limited", async () => {
    harness.db.reset({});

    // 30/minute. Unmetered, these were anonymous database reads at whatever rate
    // a caller chose — the one place on this server that was true.
    let limited = 0;
    for (let i = 0; i < 34; i++) {
        const response = await harness.get("/health/firebase");
        if (response.status === 429) limited += 1;
    }

    assert.ok(limited > 0, "expected at least one 429 within 34 requests");
});

test("the unmetered /health still answers after the probe limiter is exhausted", async () => {
    // Render's own health check must not be collateral damage of a probe flood, or
    // a scraper hitting /health/firebase would make the platform restart the
    // service.
    const response = await harness.get("/health");

    assert.equal(response.status, 200);
    assert.equal(response.body.ok, true);
});

test("a cross-origin browser is not told it may read these responses", async () => {
    const response = await harness.get("/health", { headers: { origin: "https://evil.example" } });

    // origin:false, not the default wildcard. Every real caller is a WinForms
    // desktop using HttpClient, which performs no CORS check at all, so the
    // wildcard bought nothing and told any browser it could read these responses
    // cross-origin.
    assert.equal(response.status, 200);
    assert.equal(response.headers.get("access-control-allow-origin"), null);
});

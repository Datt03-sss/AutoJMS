"use strict";

// What is actually deployed, and what would a rollback go back to. Before this
// endpoint existed the answer lived only in the Render dashboard, so a bad deploy
// had no digest to roll back to and a smoke test could not tell a stale instance
// from a fresh one.
//
// Anonymous on purpose — a smoke test runs before any token exists — which makes
// the two properties asserted here the ones that matter: it must reveal nothing
// beyond the build identity, and it must not cost a database read.

const test = require("node:test");
const assert = require("node:assert/strict");

const { FIXTURE, startServer, activeLicense } = require("./helpers/harness");

const COMMIT = "abcdef0123456789abcdef0123456789abcdef01";

let harness;

test.before(async () => {
    harness = await startServer({
        seed: { Licenses: { [FIXTURE.licenseKey]: activeLicense() } },
        env: { RENDER_GIT_COMMIT: COMMIT }
    });
});

test.after(async () => {
    await harness.close();
});

test("/api/version reports the version and the deployed commit", async () => {
    const response = await harness.get("/api/version");

    assert.equal(response.status, 200);
    assert.equal(response.body.ok, true);
    assert.equal(response.body.service, "autojms-license-server");
    assert.equal(response.body.version, require("../package.json").version);
    // The full sha, because that is what `git checkout` and a Render rollback need.
    assert.equal(response.body.commit, COMMIT);
    assert.equal(response.body.commitShort, COMMIT.slice(0, 12));
});

test("/api/version reports uptime so a stale instance is visible", async () => {
    const response = await harness.get("/api/version");

    // A deploy that half-rolled leaves one instance serving the old commit. Uptime
    // is what distinguishes "just started" from "has been up for days" when two
    // consecutive polls disagree about the commit.
    assert.equal(typeof response.body.startedAt, "number");
    assert.equal(typeof response.body.uptimeSeconds, "number");
    assert.ok(response.body.uptimeSeconds >= 0);
    assert.equal(response.body.node, process.version);
});

test("/health/version serves the same payload under the health prefix", async () => {
    const api = await harness.get("/api/version");
    const health = await harness.get("/health/version");

    assert.equal(health.status, 200);
    // Same fields, so an uptime monitor already scoped to /health/* needs no new
    // allow-list entry and no second parser.
    assert.deepEqual(Object.keys(health.body).sort(), Object.keys(api.body).sort());
    assert.equal(health.body.commit, api.body.commit);
});

test("the version endpoint answers without touching the database", async () => {
    harness.db.reset({});

    await harness.get("/api/version");
    await harness.get("/health/version");

    // Same reason /health takes no read: this is polled by monitors, and a read
    // here would bill Firebase at whatever interval a scraper chooses.
    assert.deepEqual(harness.db.reads(), []);
});

test("the version endpoint reveals nothing beyond the build identity", async () => {
    harness.db.reset({ Licenses: { [FIXTURE.licenseKey]: activeLicense() } });

    const response = await harness.get("/api/version");

    assert.deepEqual(Object.keys(response.body).sort(), [
        "commit",
        "commitShort",
        "node",
        "ok",
        "service",
        "startedAt",
        "time",
        "uptimeSeconds",
        "version"
    ]);
    // No license key, and no infrastructure hostname either — the database URL is
    // exactly the kind of field that used to leak through a health response.
    assert.ok(!response.text.includes(FIXTURE.licenseKey));
    assert.ok(!response.text.includes("firebaseio.test"));
});

test("the version endpoint is rate limited", async () => {
    // Shares healthLimiter with the Firebase probes: 30/minute. Anonymous and
    // unmetered, it would be a free way to poll for a deploy landing.
    let limited = 0;
    for (let i = 0; i < 34; i++) {
        const response = await harness.get("/api/version");
        if (response.status === 429) limited += 1;
    }

    assert.ok(limited > 0, "expected at least one 429 within 34 requests");
});

test("a build with no commit variable says so instead of guessing", async () => {
    // Reported as "unknown" rather than omitted or empty: a smoke test that reads
    // an absent field cannot tell "not deployed by CI" from "endpoint is broken".
    const bare = await startServer({ seed: {} });

    try {
        const response = await bare.get("/api/version");

        assert.equal(response.body.commit, "unknown");
        assert.equal(response.body.commitShort, "unknown");
    } finally {
        await bare.close();
    }
});

"use strict";

// The heartbeat is the only place a license can expire on a station that is
// already running. verify-license runs at launch and nowhere else, so before the
// lifecycle gate below existed, a station left open kept minting a fresh
// 60-minute token every minute for a license that had expired days earlier —
// revoking a license only took effect if the customer happened to restart.
// Those are the tests that matter here; the rest guard the replay and session
// checks that were already in place.

const test = require("node:test");
const assert = require("node:assert/strict");

const { FIXTURE, startServer, seedWithActiveSession, daysAgo } = require("./helpers/harness");

let harness;

test.before(async () => {
    harness = await startServer({ seed: seedWithActiveSession() });
});

test.after(async () => {
    await harness.close();
});

/** Each test starts from a known database; the fake also forgets its call log. */
const reseed = (licenseOverrides, sessionOverrides) =>
    harness.db.reset(seedWithActiveSession(licenseOverrides, sessionOverrides));

const beat = token => harness.post("/api/heartbeat", { token });

test("a live session continues and receives a fresh token", async () => {
    reseed();

    const response = await beat(harness.signToken());

    assert.equal(response.status, 200);
    assert.equal(response.body.action, "continue");
    assert.equal(response.body.tier, "ULTRA");

    // The returned token must be independently verifiable, or the client's next
    // heartbeat fails and the station dies a minute later.
    const claims = harness.verifyJwt(response.body.payload);
    assert.equal(claims.key, FIXTURE.licenseKey);
    assert.equal(claims.hwid, FIXTURE.hwid);
    assert.equal(claims.sid, FIXTURE.sessionId);
    assert.equal(claims.tier, "ULTRA");
    assert.ok(claims.jti, "every issued token must carry a jti");
});

test("the same token twice is refused as a replay", async () => {
    reseed();
    const token = harness.signToken();

    const first = await beat(token);
    const second = await beat(token);

    assert.equal(first.status, 200);
    assert.equal(second.status, 401);
    assert.equal(second.body.action, "kill");
});

test("a token with no jti is refused", async () => {
    reseed();

    // jtiCache.has(undefined) is always false and set(undefined, ...) records
    // nothing, so without an explicit check such a token replayed forever.
    const response = await beat(harness.signToken({ jti: null }));

    assert.equal(response.status, 401);
    assert.equal(response.body.action, "kill");
});

test("a missing Authorization header is refused", async () => {
    reseed();

    const response = await harness.post("/api/heartbeat");

    assert.equal(response.status, 401);
    assert.equal(response.body.action, "kill");
});

test("a token signed for another audience is refused", async () => {
    reseed();

    const response = await beat(harness.signToken({ audience: "somebody-elses-client" }));

    assert.equal(response.status, 401);
    assert.equal(response.body.action, "kill");
});

test("an expired license is killed and its session is removed", async () => {
    // 40 days past expiry with the default 7-day grace: well past the window.
    reseed({ expiresAt: daysAgo(40) });

    const response = await beat(harness.signToken());

    assert.equal(response.status, 401);
    assert.equal(response.body.action, "kill");
    assert.equal(response.body.error, "LICENSE_EXPIRED");
    assert.ok(response.body.expiresAt, "the client needs the date to show the user");

    // Refusing the token alone is not enough: the session is what the Sheets
    // broker and the DataHub assertion route authenticate against, so leaving it
    // active would keep both of those working for a dead license.
    assert.equal(harness.db.read(`sessions/${FIXTURE.sessionId}`), null);
    assert.ok(harness.db.removed(`sessions/${FIXTURE.sessionId}`));
});

test("a license inside its grace window continues, and says so", async () => {
    reseed({ expiresAt: daysAgo(3), graceDays: 7 });

    const response = await beat(harness.signToken());

    assert.equal(response.status, 200);
    assert.equal(response.body.action, "continue");
    assert.equal(response.body.effectiveStatus, "grace");
    assert.ok(response.body.graceUntil);
    // Negative: the expiry is in the past. The client uses this to decide between
    // "renew soon" and "renew now".
    assert.ok(response.body.daysRemaining < 0);
    assert.notEqual(harness.db.read(`sessions/${FIXTURE.sessionId}`), null);
});

test("a license past its grace window is killed", async () => {
    // Explicit graceDays: 0 — the boundary a record can set per key.
    reseed({ expiresAt: daysAgo(1), graceDays: 0 });

    const response = await beat(harness.signToken());

    assert.equal(response.status, 401);
    assert.equal(response.body.error, "LICENSE_EXPIRED");
});

test("a license with no expiry keeps working", async () => {
    // The fleet in the field has no expiresAt yet. Treating that as expired would
    // take every station offline the moment this shipped.
    reseed({ expiresAt: undefined });

    const response = await beat(harness.signToken());

    assert.equal(response.status, 200);
    assert.equal(response.body.action, "continue");
    assert.equal(response.body.effectiveStatus, "active");
    assert.equal(response.body.expiresAt, null);
});

test("a locked license is killed even though its expiry is in the future", async () => {
    reseed({ status: "locked", expiresAt: daysAgo(-30) });

    const response = await beat(harness.signToken());

    assert.equal(response.status, 401);
    assert.equal(response.body.error, "LICENSE_INACTIVE");
});

test("a deleted license row kills the session", async () => {
    harness.db.reset({ sessions: seedWithActiveSession().sessions });

    const response = await beat(harness.signToken());

    assert.equal(response.status, 401);
    assert.equal(response.body.error, "LICENSE_NOT_FOUND");
    assert.equal(harness.db.read(`sessions/${FIXTURE.sessionId}`), null);
});

test("a revoked session is killed", async () => {
    harness.db.reset({ Licenses: seedWithActiveSession().Licenses });

    const response = await beat(harness.signToken());

    assert.equal(response.status, 401);
    assert.equal(response.body.action, "kill");
});

test("a session marked inactive is killed", async () => {
    reseed({}, { status: "revoked" });

    const response = await beat(harness.signToken());

    assert.equal(response.status, 401);
    assert.equal(response.body.action, "kill");
});

test("a license re-bound to another machine kills this station", async () => {
    reseed({ hwid: "ffffffffffffffffffffffffffffffff" });

    const response = await beat(harness.signToken());

    assert.equal(response.status, 401);
    assert.equal(response.body.error, "HWID_MISMATCH");
    assert.equal(harness.db.read(`sessions/${FIXTURE.sessionId}`), null);
});

test("a successful beat records lastPing", async () => {
    reseed({}, { lastPing: 1 });

    const response = await beat(harness.signToken());

    assert.equal(response.status, 200);
    assert.ok(harness.db.read(`sessions/${FIXTURE.sessionId}`).lastPing > 1);
});

test("the tier comes from the token, not from a license edited mid-session", async () => {
    // Owner decision (2026-08-24): a tier change takes effect on restart. The
    // client caches its entitlement at launch, so reading the record here would
    // make the heartbeat disagree with the running app rather than change it.
    reseed({ tier: "BASE" });

    const response = await beat(harness.signToken({ tier: "ULTRA" }));

    assert.equal(response.status, 200);
    assert.equal(response.body.tier, "ULTRA");
});

test("a Firebase outage is a 5xx, not a kill", async () => {
    reseed();
    harness.db.failNextWith("FIREBASE_SESSION_READ_TIMEOUT");

    const response = await beat(harness.signToken());

    // A transient backend failure must never read as "your license is invalid".
    // The client still treats any non-200 as fatal (LicenseApiService is a
    // protected file), but the server side of that fix is asserted here.
    assert.equal(response.status, 503);
    assert.notEqual(response.body.action, "kill");
});

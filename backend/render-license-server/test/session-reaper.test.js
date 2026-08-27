"use strict";

// ==========================================================================
// verify-license reaps this licence's orphaned sessions.
// ==========================================================================
// Nothing else deleted a session row. /api/logout removes the one it holds a token
// for, and verify-license removed the previous row for the SAME device — so a
// session that ended any other way (crash, power cut, laptop closed, station
// reimaged with a new hwid) stayed in /sessions forever, growing the node that
// every login scans.
//
// The reason this file is mostly about what the reaper does NOT delete: removing a
// session is not a soft action. Heartbeat answers a missing row with 401
// `action: "kill"` and the client shuts itself down. So a reaper that is merely
// approximately right kicks working stations out of a running shift, and the
// safety tests below are the point of the feature, not its edge cases.
// ==========================================================================

const test = require("node:test");
const assert = require("node:assert/strict");

const { FIXTURE, startServer, activeLicense, activeSession } = require("./helpers/harness");

const OTHER_DEVICE = "ffffffffffffffffffffffffffffffff";
const OTHER_LICENSE = "AJMS-TEST-0002-KEY9";

let harness;
let staleSessionMs;

test.before(async () => {
    harness = await startServer();
    staleSessionMs = harness.app.staleSessionMs;
});

test.after(async () => {
    await harness.close();
});

/** Seeds one active licence plus whatever session rows the test needs. */
const seedSessions = sessions =>
    harness.db.reset({
        Licenses: { [FIXTURE.licenseKey]: activeLicense() },
        sessions
    });

const verify = () =>
    harness.post("/api/verify-license", {
        body: { licenseKey: FIXTURE.licenseKey, hwid: FIXTURE.hwid }
    });

/** A session on another device, dated relative to the stale threshold. */
const otherDevice = (msPastThreshold, overrides = {}) =>
    activeSession({
        hwid: OTHER_DEVICE,
        createdAt: Date.now() - staleSessionMs - msPastThreshold,
        lastPing: Date.now() - staleSessionMs - msPastThreshold,
        ...overrides
    });

test("the threshold is derived from the access token TTL, not chosen next to it", () => {
    // The safety argument only holds while this is true: lastPing is written solely
    // by /api/heartbeat, and that route requires an unexpired token AND mints a
    // fresh one. So beyond one TTL of silence, no token the station could still
    // hold is valid, and its next heartbeat is a 401 regardless of this reaper.
    // Anything SHORTER than one TTL would delete sessions that can still beat.
    assert.equal(harness.app.accessTokenTtlMs, 60 * 60_000);
    assert.equal(harness.app.staleSessionMs, harness.app.accessTokenTtlMs * 2);
    assert.ok(harness.app.staleSessionMs >= harness.app.accessTokenTtlMs);
});

test("an orphan from another device on this licence is removed", async () => {
    seedSessions({ "orphan-1": otherDevice(60_000) });

    const response = await verify();

    assert.equal(response.status, 200);
    assert.equal(harness.db.read("sessions/orphan-1"), null);
});

test("a session that is merely quiet is left alone", async () => {
    // One second INSIDE the threshold. This is the test that stops the reaper from
    // being a remote kill switch: that station's token is still valid, its next
    // heartbeat would have succeeded, and deleting the row makes it shut down.
    const key = "live-other-device";
    seedSessions({
        [key]: activeSession({
            hwid: OTHER_DEVICE,
            createdAt: Date.now() - staleSessionMs + 1000,
            lastPing: Date.now() - staleSessionMs + 1000
        })
    });

    await verify();

    assert.ok(harness.db.read(`sessions/${key}`), "a session inside the threshold must survive");
});

test("a session whose age cannot be established is left alone", async () => {
    // No lastPing and no createdAt. An unknown age is not evidence of death, and the
    // cost of guessing wrong is a 401 kill — so an undatable row is kept, and shows
    // up as a row that never disappears rather than as a station that shut down.
    const bare = { licenseKey: FIXTURE.licenseKey, hwid: OTHER_DEVICE, tier: "ULTRA", status: "active" };
    seedSessions({ undatable: bare });

    await verify();

    assert.ok(harness.db.read("sessions/undatable"), "a row with no timestamps must survive");

    // Same for values that are present but not usable as a timestamp. Number("")
    // and Number(null) are both 0 — 1 January 1970, the most stale value there is —
    // so a blank or missing field read through Number() would have deleted the row
    // it could not date. "1700000000000" is the hand-edited-in-the-console case: a
    // numeric string is old enough to reap on paper, and is still kept, because the
    // safe direction for a value this code did not write is to leave it alone.
    for (const bad of ["", "not-a-number", "1700000000000", null, {}]) {
        seedSessions({ undatable: { ...bare, lastPing: bad, createdAt: bad } });
        await verify();
        assert.ok(
            harness.db.read("sessions/undatable"),
            `lastPing/createdAt of ${JSON.stringify(bad)} must not be read as an old date`
        );
    }
});

test("createdAt stands in for a session that never pinged", async () => {
    // A row written by verify-license and abandoned before its first heartbeat has
    // no lastPing at all. Judging it by createdAt is what makes those reachable;
    // requiring lastPing would leave exactly the crash-on-startup case forever.
    const stale = otherDevice(60_000);
    delete stale.lastPing;
    seedSessions({ "never-pinged": stale });

    await verify();

    assert.equal(harness.db.read("sessions/never-pinged"), null);
});

test("a fresh lastPing beats an ancient createdAt", async () => {
    // The precedence has to be this way round: a station that has been up for days
    // has an old createdAt and a current lastPing, and reading createdAt first would
    // reap the longest-running stations in the fleet — the ones least likely to be
    // dead.
    seedSessions({
        "long-running": activeSession({
            hwid: OTHER_DEVICE,
            createdAt: Date.now() - staleSessionMs * 10,
            lastPing: Date.now() - 30_000
        })
    });

    await verify();

    assert.ok(harness.db.read("sessions/long-running"), "a recently active station must survive");
});

test("orphans belonging to another licence are not touched", async () => {
    // The query is scoped by licenseKey, so this holds today. It is pinned because
    // the scope is the blast radius: a future change that widened the read would
    // silently make one customer's login delete another customer's sessions.
    seedSessions({
        "mine-stale": otherDevice(60_000),
        "theirs-stale": otherDevice(60_000, { licenseKey: OTHER_LICENSE })
    });

    await verify();

    assert.equal(harness.db.read("sessions/mine-stale"), null);
    assert.ok(harness.db.read("sessions/theirs-stale"), "another licence's row is not ours to delete");
});

test("reaping costs no extra read, and at most the update the flow already makes", async () => {
    // The whole design constraint: this runs on a single free-tier instance with no
    // background worker, so the reaper had to be free. It reuses the sessions query
    // and the multi-path update that verify-license already issues.
    seedSessions({
        "orphan-a": otherDevice(60_000),
        "orphan-b": otherDevice(120_000),
        "orphan-c": otherDevice(180_000)
    });

    await verify();

    const sessionQueries = harness.db.reads().filter(path => path.startsWith("sessions#"));
    const sessionUpdates = harness.db.writes().filter(write => write.op === "update" && write.path === "sessions");

    assert.equal(sessionQueries.length, 1, "one query, however many rows it reaps");
    assert.equal(sessionUpdates.length, 1, "one update, however many rows it reaps");
    assert.deepEqual(Object.keys(sessionUpdates[0].value).sort(), ["orphan-a", "orphan-b", "orphan-c"]);
});

test("the same-device revoke still works, and is counted apart from the reap", async () => {
    // revokedCount and reapedCount answer different questions. Several revokes on
    // every launch means a station is re-verifying instead of resuming; several
    // reaps means stations are dying without logging out. Merging them into one
    // number would hide both.
    seedSessions({
        "my-previous-run": activeSession({ createdAt: Date.now(), lastPing: Date.now() }),
        "someone-elses-orphan": otherDevice(60_000)
    });

    await verify();

    // The current device's previous row goes even though it is brand new — that is
    // the pre-existing single-device rule, not the reaper.
    assert.equal(harness.db.read("sessions/my-previous-run"), null);
    assert.equal(harness.db.read("sessions/someone-elses-orphan"), null);
});

test("a login with no orphans and nothing to revoke writes no session update", async () => {
    seedSessions({
        "live-other-device": activeSession({
            hwid: OTHER_DEVICE,
            createdAt: Date.now(),
            lastPing: Date.now()
        })
    });

    await verify();

    const sessionUpdates = harness.db.writes().filter(write => write.op === "update" && write.path === "sessions");
    assert.equal(sessionUpdates.length, 0, "nothing to delete must still cost nothing to write");
});

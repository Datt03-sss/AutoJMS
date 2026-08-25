"use strict";

// Logout used to be unauthenticated and unmetered: anyone who could reach the
// host could burn Firebase writes, and anyone who learned a session id — from a
// log line, a crash dump, a shared screenshot — could end that station's session.
// A caller must now prove it owns the session it is ending.

const test = require("node:test");
const assert = require("node:assert/strict");

const { FIXTURE, startServer, seedWithActiveSession } = require("./helpers/harness");

let harness;

test.before(async () => {
    harness = await startServer({ seed: seedWithActiveSession() });
});

test.after(async () => {
    await harness.close();
});

const reseed = () => harness.db.reset(seedWithActiveSession());

const logout = (body, token) => harness.post("/api/logout", { body, token });

test("a station ends its own session", async () => {
    reseed();

    const response = await logout({ sid: FIXTURE.sessionId }, harness.signToken());

    assert.equal(response.status, 200);
    assert.equal(response.body.ok, true);
    assert.equal(harness.db.read(`sessions/${FIXTURE.sessionId}`), null);
});

test("a request with no token cannot end a session", async () => {
    reseed();

    const response = await logout({ sid: FIXTURE.sessionId });

    assert.equal(response.status, 401);
    assert.equal(response.body.error, "UNAUTHORIZED");
    // The session survives — that is the whole point.
    assert.notEqual(harness.db.read(`sessions/${FIXTURE.sessionId}`), null);
});

test("a token cannot end somebody else's session", async () => {
    harness.db.reset({
        ...seedWithActiveSession(),
        sessions: {
            ...seedWithActiveSession().sessions,
            "another-stations-session": { licenseKey: "OTHER", hwid: "other", status: "active" }
        }
    });

    const response = await logout({ sid: "another-stations-session" }, harness.signToken());

    assert.equal(response.status, 403);
    assert.equal(response.body.error, "SESSION_MISMATCH");
    assert.notEqual(harness.db.read("sessions/another-stations-session"), null);
});

test("an invalid token cannot end a session", async () => {
    reseed();

    const response = await logout({ sid: FIXTURE.sessionId }, "not-a-jwt");

    assert.equal(response.status, 401);
    assert.equal(response.body.error, "UNAUTHORIZED");
    assert.notEqual(harness.db.read(`sessions/${FIXTURE.sessionId}`), null);
});

test("an expired token cannot end a session", async () => {
    reseed();

    // The client logs out at shutdown, which may be long after the last refresh.
    // Refusing is still correct: an abandoned session is reaped by expiry, while
    // accepting a dead token would reopen the hole this route was closed for.
    const response = await logout({ sid: FIXTURE.sessionId }, harness.signToken({ expiresIn: "-1m" }));

    assert.equal(response.status, 401);
    assert.notEqual(harness.db.read(`sessions/${FIXTURE.sessionId}`), null);
});

test("a logout with no sid is a no-op and writes nothing", async () => {
    reseed();

    const response = await logout({});

    // Answered 200 without authentication on purpose: there is nothing to
    // authorise, and the client calls this on a shutdown path where a 401 would
    // surface as a spurious error dialog.
    assert.equal(response.status, 200);
    assert.equal(response.body.ok, true);
    assert.deepEqual(harness.db.writes(), []);
});

test("logging out twice is not an error", async () => {
    reseed();
    const token = harness.signToken();

    const first = await logout({ sid: FIXTURE.sessionId }, token);
    const second = await logout({ sid: FIXTURE.sessionId }, token);

    // The session is already gone; a station that retries its shutdown must not
    // see a failure.
    assert.equal(first.status, 200);
    assert.equal(second.status, 200);
});

test("a Firebase outage is reported, not swallowed", async () => {
    reseed();
    harness.db.failNextWith("FIREBASE_SESSION_REMOVE_TIMEOUT");

    const response = await logout({ sid: FIXTURE.sessionId }, harness.signToken());

    assert.equal(response.status, 503);
    assert.equal(response.body.error, "FIREBASE_TIMEOUT");
});

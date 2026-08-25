"use strict";

// The Sheets broker hands a real Google access token to a desktop station. Before
// the lifecycle gate, it did that for any license whose session was still alive —
// and the heartbeat refreshed that session every minute, so an expired license
// kept receiving Google credentials for as long as the app stayed open.
//
// Every rejection test therefore also asserts that NO token was minted. A 403
// that still called Google would leak the very thing the gate exists to protect.

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

const reseed = (licenseOverrides, sessionOverrides) => {
    harness.db.reset(seedWithActiveSession(licenseOverrides, sessionOverrides));
    harness.google.resetCalls();
};

const grant = token => harness.post("/api/google-sheets/grant", { token });

test("an active license receives a token and its spreadsheet id", async () => {
    reseed();

    const response = await grant(harness.signToken());

    assert.equal(response.status, 200);
    assert.equal(response.body.ok, true);
    assert.equal(response.body.accessToken, FIXTURE.googleAccessToken);
    assert.equal(response.body.spreadsheetId, FIXTURE.spreadsheetId);
    assert.deepEqual(response.body.scopes, ["https://www.googleapis.com/auth/spreadsheets"]);
    // The client refreshes on this, so an absent or zero expiry means it either
    // never refreshes or hammers the broker.
    assert.ok(response.body.expiresInSeconds >= 60);
    assert.ok(Date.parse(response.body.expiresAt) > Date.now());
});

test("BASE receives a token as well as ULTRA", async () => {
    // Owner decision (2026-08-24): Google Sheets is not tier-gated. Asserted so a
    // later tier sweep cannot quietly take it away from BASE.
    reseed({ tier: "BASE" }, { tier: "BASE" });

    const response = await grant(harness.signToken({ tier: "BASE" }));

    assert.equal(response.status, 200);
    assert.equal(response.body.accessToken, FIXTURE.googleAccessToken);
});

test("an expired license is refused and no Google token is minted", async () => {
    reseed({ expiresAt: daysAgo(40) });

    const response = await grant(harness.signToken());

    assert.equal(response.status, 403);
    assert.equal(response.body.error, "LICENSE_EXPIRED");
    assert.ok(response.body.expiresAt);
    assert.equal(harness.google.accessTokenCalls(), 0);
});

test("a license in grace still receives a token", async () => {
    // Grace is "warn, keep working". Cutting Sheets off during it would break the
    // customer's data entry days before the licence actually dies.
    reseed({ expiresAt: daysAgo(2) });

    const response = await grant(harness.signToken());

    assert.equal(response.status, 200);
    assert.equal(harness.google.accessTokenCalls(), 1);
});

test("a locked license is refused before the expiry check", async () => {
    reseed({ status: "locked" });

    const response = await grant(harness.signToken());

    assert.equal(response.status, 403);
    assert.equal(response.body.error, "LICENSE_INACTIVE");
    assert.equal(harness.google.accessTokenCalls(), 0);
});

test("a deleted license row is a 404", async () => {
    harness.db.reset({ sessions: seedWithActiveSession().sessions });
    harness.google.resetCalls();

    const response = await grant(harness.signToken());

    assert.equal(response.status, 404);
    assert.equal(response.body.error, "LICENSE_NOT_FOUND");
    assert.equal(harness.google.accessTokenCalls(), 0);
});

test("a missing bearer token is refused", async () => {
    reseed();

    const response = await harness.post("/api/google-sheets/grant");

    assert.equal(response.status, 401);
    assert.equal(response.body.error, "UNAUTHORIZED");
    assert.equal(harness.google.accessTokenCalls(), 0);
});

test("a revoked session is refused", async () => {
    reseed();
    harness.db.reset({ Licenses: seedWithActiveSession().Licenses });

    const response = await grant(harness.signToken());

    assert.equal(response.status, 401);
    assert.equal(response.body.error, "SESSION_NOT_FOUND");
    assert.equal(harness.google.accessTokenCalls(), 0);
});

test("a session belonging to another machine is refused", async () => {
    // The session record, not the license record: a token whose hwid claim has
    // been edited must not authenticate against a session bound elsewhere.
    reseed({}, { hwid: "ffffffffffffffffffffffffffffffff" });

    const response = await grant(harness.signToken());

    assert.equal(response.status, 401);
    assert.equal(response.body.error, "SESSION_INACTIVE");
    assert.equal(harness.google.accessTokenCalls(), 0);
});

test("a session for a different license is refused", async () => {
    reseed({}, { licenseKey: "SOME-OTHER-LICENSE" });

    const response = await grant(harness.signToken());

    assert.equal(response.status, 401);
    assert.equal(response.body.error, "SESSION_INACTIVE");
});

test("a token signed by another key is refused", async () => {
    reseed();
    const other = await startServer({ seed: seedWithActiveSession() });

    try {
        // Same claims, different signing key: the only thing separating a real
        // station from anyone who can read a session id out of a log.
        const response = await grant(other.signToken());

        assert.equal(response.status, 401);
        assert.equal(response.body.error, "UNAUTHORIZED");
        assert.equal(harness.google.accessTokenCalls(), 0);
    } finally {
        await other.close();
    }
});

test("a license with no spreadsheet configured still gets a token", async () => {
    // The station falls back to its local config for the id; refusing the token
    // would break Sheets for every license that has not been migrated.
    reseed({ dataSpreadsheetId: undefined });

    const response = await grant(harness.signToken());

    assert.equal(response.status, 200);
    assert.equal(response.body.spreadsheetId, "");
});

test("a Google outage is reported as a broker failure, not a license failure", async () => {
    reseed();
    harness.google.state.getAccessTokenError = "GOOGLE_SHEETS_ACCESS_TOKEN_EMPTY";

    try {
        const response = await grant(harness.signToken());

        // 503, and never a 401/403: the station must retry rather than tell the
        // customer their license is invalid.
        assert.equal(response.status, 503);
        assert.equal(response.body.error, "GOOGLE_SHEETS_BROKER_UNAVAILABLE");
    } finally {
        harness.google.state.getAccessTokenError = null;
    }
});

test("a Firebase outage is a 503, not a rejection", async () => {
    reseed();
    harness.db.failNextWith("FIREBASE_GOOGLE_SHEETS_SESSION_READ_TIMEOUT");

    const response = await grant(harness.signToken());

    assert.equal(response.status, 503);
    assert.equal(response.body.error, "GOOGLE_SHEETS_TIMEOUT");
    assert.equal(harness.google.accessTokenCalls(), 0);
});

"use strict";

// An assertion buys a 24-hour DataHub device token, and a station re-enrolls with
// it for as long as it stays open. That makes this route the one place where an
// expired license could keep renewing write access to the whole data plane, one
// assertion at a time — hence the lifecycle gate, and the tests for it here.
//
// The wire format is also asserted end to end: RsaLicenseAssertionValidator on the
// VPS parses `v1rs256.<base64url payload>.<base64url signature>` with
// case-sensitive PascalCase keys, so a rename in either half is a 401 with no
// explanation.

const test = require("node:test");
const assert = require("node:assert/strict");
const crypto = require("node:crypto");

const { FIXTURE, startServer, seedWithActiveSession, activeLicense, daysAgo } = require("./helpers/harness");

let harness;

test.before(async () => {
    harness = await startServer({ seed: seedWithActiveSession() });
});

test.after(async () => {
    await harness.close();
});

const reseed = (licenseOverrides, sessionOverrides) =>
    harness.db.reset(seedWithActiveSession(licenseOverrides, sessionOverrides));

const reassert = token => harness.post("/api/datahub/license-assertion", { token });

/** Decodes and signature-checks an assertion exactly as the VPS validator does. */
function openAssertion(assertion, publicKey) {
    const [prefix, encodedPayload, signature] = String(assertion).split(".");
    assert.equal(prefix, "v1rs256");

    const valid = crypto.verify(
        "sha256",
        Buffer.from(encodedPayload, "utf8"),
        { key: publicKey, padding: crypto.constants.RSA_PKCS1_PADDING },
        Buffer.from(signature, "base64url")
    );
    assert.ok(valid, "signature must verify with the public key the VPS holds");

    return JSON.parse(Buffer.from(encodedPayload, "base64url").toString("utf8"));
}

test("a live session is re-issued a verifiable assertion", async () => {
    reseed();

    const response = await reassert(harness.signToken());

    assert.equal(response.status, 200);
    assert.equal(response.body.apiBaseUrl, FIXTURE.datahubUrl);
    assert.equal(response.body.siteCode, FIXTURE.siteCode);

    const payload = openAssertion(response.body.licenseAssertion, harness.datahubPublicKey);
    assert.equal(payload.Issuer, FIXTURE.assertionIssuer);
    assert.equal(payload.Audience, FIXTURE.assertionAudience);
    assert.equal(payload.Channel, FIXTURE.channel);
    assert.deepEqual(payload.SiteCodes, [FIXTURE.siteCode]);
    assert.equal(response.body.assertionExpiresAt, payload.ExpiresAt);
    // 300s default, bounded to [60, 3600]. Short by design: an assertion is a
    // bearer credential for enrollment.
    assert.ok(payload.ExpiresAt - Math.floor(Date.now() / 1000) <= 300);
});

test("the same access token may be used twice", async () => {
    reseed();
    const token = harness.signToken();

    const first = await reassert(token);
    const second = await reassert(token);

    // Deliberate: the heartbeat owns replay detection, and burning the jti here
    // would kill the very session that is asking to stay connected. Asserted so
    // nobody "fixes" it into a replay check by symmetry with the heartbeat.
    assert.equal(first.status, 200);
    assert.equal(second.status, 200);
});

test("siteCodes wins over middleCode when a license lists several sites", async () => {
    reseed({ siteCodes: ["hn01", "  sg02 ", "hn01"] });

    const response = await reassert(harness.signToken());

    // Uppercased and de-duplicated the same way the VPS validator normalises them,
    // or an enrollment for a legitimately licensed site is refused.
    const payload = openAssertion(response.body.licenseAssertion, harness.datahubPublicKey);
    assert.deepEqual(payload.SiteCodes, ["HN01", "SG02"]);
    assert.equal(response.body.siteCode, "HN01");
});

test("seats and tokenVersion are clamped rather than trusted", async () => {
    reseed({ seats: 99_999, tokenVersion: -4 });

    const response = await reassert(harness.signToken());

    const payload = openAssertion(response.body.licenseAssertion, harness.datahubPublicKey);
    assert.equal(payload.Seats, 500);
    assert.equal(payload.TokenVersion, 1);
});

test("an expired license cannot renew its data-plane access", async () => {
    reseed({ expiresAt: daysAgo(40) });

    const response = await reassert(harness.signToken());

    assert.equal(response.status, 403);
    assert.equal(response.body.error, "LICENSE_EXPIRED");
    assert.equal(response.body.licenseAssertion, undefined);
});

test("a license in grace can still renew", async () => {
    reseed({ expiresAt: daysAgo(3) });

    const response = await reassert(harness.signToken());

    assert.equal(response.status, 200);
    assert.ok(response.body.licenseAssertion);
});

test("a locked license is refused", async () => {
    reseed({ status: "locked" });

    const response = await reassert(harness.signToken());

    assert.equal(response.status, 401);
    assert.equal(response.body.error, "LICENSE_INACTIVE");
});

test("a license re-bound to another machine is refused", async () => {
    reseed({ hwid: "ffffffffffffffffffffffffffffffff" });

    const response = await reassert(harness.signToken());

    assert.equal(response.status, 401);
    assert.equal(response.body.error, "HWID_MISMATCH");
});

test("a revoked session is refused", async () => {
    reseed({}, { status: "revoked" });

    const response = await reassert(harness.signToken());

    assert.equal(response.status, 401);
    assert.equal(response.body.error, "SESSION_REVOKED");
});

test("a missing bearer token is refused", async () => {
    reseed();

    const response = await harness.post("/api/datahub/license-assertion");

    assert.equal(response.status, 401);
    assert.equal(response.body.error, "UNAUTHORIZED");
});

test("an expired access token is refused", async () => {
    reseed();

    const response = await reassert(harness.signToken({ expiresIn: "-1m" }));

    assert.equal(response.status, 401);
    assert.equal(response.body.error, "TOKEN_INVALID");
});

test("a license with no site code cannot enroll", async () => {
    // "Cannot enroll", never "enroll unrestricted": an assertion with an empty
    // SiteCodes list would be a credential for every tenant on the VPS.
    reseed({ middleCode: "", siteCodes: [], siteId: "" });

    const response = await reassert(harness.signToken());

    assert.equal(response.status, 503);
    assert.equal(response.body.error, "ASSERTION_UNAVAILABLE");
});

test("a deployment with no signing key refuses to enroll", async () => {
    const degraded = await startServer({
        withAssertionKey: false,
        seed: {
            Licenses: { [FIXTURE.licenseKey]: activeLicense() },
            sessions: seedWithActiveSession().sessions
        }
    });

    try {
        const response = await degraded.post("/api/datahub/license-assertion", {
            token: degraded.signToken()
        });

        assert.equal(response.status, 503);
        assert.equal(response.body.error, "ASSERTION_UNAVAILABLE");
    } finally {
        await degraded.close();
    }
});

test("a non-https DataHub URL is omitted from the claim rather than signed", async () => {
    // The validator rejects any scheme other than https, and a claim it rejects
    // fails the whole enrollment. Absent is the only safe encoding.
    const insecure = await startServer({
        env: { DATAHUB_API_BASE_URL: "http://datahub.test.local" },
        seed: seedWithActiveSession()
    });

    try {
        const response = await insecure.post("/api/datahub/license-assertion", {
            token: insecure.signToken()
        });

        assert.equal(response.status, 200);
        const payload = openAssertion(response.body.licenseAssertion, insecure.datahubPublicKey);
        assert.equal(payload.DataHubUrl, null);
    } finally {
        await insecure.close();
    }
});

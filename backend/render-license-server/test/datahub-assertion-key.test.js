"use strict";

// Provisioning a DataHub host means giving it the PUBLIC half of the key this
// service signs assertions with. That half is derivable only from the private key,
// the private key is `sync: false` so it exists only in the Render dashboard, and
// the free tier has no shell — so before this endpoint the sole supply route was a
// human still holding the .pub file from `openssl rsa -pubout`. When
// dev.jmsauto.online was rebuilt on 2026-09-07 nobody was: the VPS came back with
// DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY empty and every enrollment answered 401.
//
// The property that actually matters is not "returns a PEM" — it is that the PEM
// returned verifies what this service signs. A correct-looking key from the wrong
// pair fails identically to no key at all, so the first test mints a real assertion
// and verifies it with the published key rather than comparing strings.

const test = require("node:test");
const assert = require("node:assert/strict");
const crypto = require("node:crypto");

const { FIXTURE, startServer, seedWithActiveSession } = require("./helpers/harness");

const ROUTE = "/health/datahub-assertion-key";

let harness;

test.before(async () => {
    harness = await startServer({ seed: seedWithActiveSession() });
});

test.after(async () => {
    await harness.close();
});

test("the published key verifies an assertion this service actually signed", async () => {
    const published = await harness.get(ROUTE);
    assert.equal(published.status, 200);
    assert.equal(published.body.ok, true);
    assert.equal(published.body.algorithm, "RS256");

    const verified = await harness.post("/api/verify-license", {
        body: {
            licenseKey: FIXTURE.licenseKey,
            hwid: FIXTURE.hwid,
            sessionId: FIXTURE.sessionId
        }
    });
    assert.equal(verified.status, 200);

    const assertionValue = verified.body.datahub.licenseAssertion;
    const [prefix, payload, signature] = assertionValue.split(".");
    assert.equal(prefix, "v1rs256");

    // Exactly the check RsaLicenseAssertionValidator performs on the VPS: PKCS#1 v1.5
    // over the encoded payload, SHA-256.
    const signatureIsValid = crypto.verify(
        "sha256",
        Buffer.from(payload, "utf8"),
        { key: published.body.publicKey, padding: crypto.constants.RSA_PKCS1_PADDING },
        Buffer.from(signature, "base64url")
    );

    assert.equal(signatureIsValid, true);
});

test("the published key is the public half of the signing pair, not a lookalike", async () => {
    const response = await harness.get(ROUTE);

    // The harness holds the pair it generated, so this pins the exact bytes rather
    // than only proving some RSA key round-trips.
    assert.equal(
        response.body.publicKey,
        harness.datahubPublicKey.trim()
    );
    assert.equal(response.body.modulusBits, 2048);
});

test("the fingerprint is comparable with what an operator can compute themselves", async () => {
    const response = await harness.get(ROUTE);

    const expected = `sha256:${crypto
        .createHash("sha256")
        .update(crypto.createPublicKey(harness.datahubPublicKey).export({ type: "spki", format: "der" }))
        .digest("base64")}`;

    assert.equal(response.body.fingerprint, expected);
});

test("no private material is exposed", async () => {
    const response = await harness.get(ROUTE);

    assert.match(response.body.publicKey, /^-----BEGIN PUBLIC KEY-----/);
    assert.doesNotMatch(response.text, /PRIVATE KEY/);
});

test("an unconfigured service says so with 503 instead of a misleading 200", async () => {
    // 503 and not 200-with-a-null: a provisioning script that reads .publicKey off a
    // 200 would write the string "undefined" into the VPS env and produce the same
    // 401 this endpoint exists to end.
    const closed = await startServer({
        seed: seedWithActiveSession(),
        withAssertionKey: false
    });

    try {
        const response = await closed.get(ROUTE);

        assert.equal(response.status, 503);
        assert.equal(response.body.ok, false);
        assert.equal(response.body.configured, false);
        assert.equal(response.body.publicKey, undefined);
    } finally {
        await closed.close();
    }
});

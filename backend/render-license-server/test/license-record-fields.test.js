"use strict";

// ==========================================================================
// Fields on the Licenses/{key} record that the server reads but nothing tested.
// ==========================================================================
// Each of these was a field the v2 schema publishes and the server quietly
// ignored or mis-handled, which is the worst shape a config bug can take: the
// record says one thing, the fleet does another, and no error is raised on
// either side. The tests are here so a future edit that re-breaks the link
// fails loudly.
//
// This lives in its own file on purpose — `node --test` runs each FILE in its
// own process, so it gets its own verify-license rate-limiter budget (20/min)
// instead of eating into verify-license.test.js's.
// ==========================================================================

const test = require("node:test");
const assert = require("node:assert/strict");
const crypto = require("node:crypto");

const { FIXTURE, startServer, seedWithActiveSession } = require("./helpers/harness");

let harness;

test.before(async () => {
    harness = await startServer({ seed: seedWithActiveSession() });
});

test.after(async () => {
    await harness.close();
});

const reseed = licenseOverrides =>
    harness.db.reset(seedWithActiveSession(licenseOverrides));

const verify = () =>
    harness.post("/api/verify-license", {
        body: { licenseKey: FIXTURE.licenseKey, hwid: FIXTURE.hwid }
    });

/** Decodes an assertion the way RsaLicenseAssertionValidator on the VPS does. */
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

// ---- offlineGraceHours ---------------------------------------------------

test("offlineGraceHours on the record wins over the fleet default", async () => {
    // The v2 schema publishes this per key and the docs describe it as per key,
    // but the response was hardcoded to the env default — a key sold with a
    // longer offline window silently got the standard one.
    reseed({ offlineGraceHours: 168 });

    const response = await verify();

    assert.equal(response.status, 200);
    assert.equal(response.body.license.offlineGraceHours, 168);
});

test("a record with no offlineGraceHours gets the fleet default", async () => {
    reseed();

    const response = await verify();

    assert.equal(response.status, 200);
    assert.equal(response.body.license.offlineGraceHours, 72);
});

test("an unusable offlineGraceHours falls back rather than becoming zero", async () => {
    // Number(null) === 0 and Number("") === 0. Coercing either would hand the
    // station a zero-hour offline window, i.e. revoke offline use entirely, for
    // a field the operator only failed to fill in.
    for (const bad of [null, "", "soon", {}]) {
        reseed({ offlineGraceHours: bad });

        const response = await verify();

        assert.equal(response.status, 200);
        assert.equal(
            response.body.license.offlineGraceHours,
            72,
            `offlineGraceHours=${JSON.stringify(bad)} must fall back to the default`
        );
    }
});

// ---- placeholder site codes ---------------------------------------------

test("a placeholder site code is refused a DataHub assertion but still signs in", async () => {
    // middleCode IS the DataHub tenant key. "0000" is truthy, so it used to pass
    // the site-code filter and every licence still carrying the placeholder was
    // minted an assertion for the SAME tenant — those customers would read and
    // write each other's rows. Login is deliberately unaffected: the station
    // works locally, it just has no data-plane credential.
    reseed({ middleCode: "0000" });

    const response = await verify();

    assert.equal(response.status, 200);
    assert.equal(response.body.datahub.licenseAssertion, "");
    assert.equal(response.body.datahub.assertionExpiresAt, 0);
});

test("every placeholder spelling is refused, not just the empty string", async () => {
    for (const middleCode of ["0000", "00000", "0", "default", " none ", "TBD"]) {
        reseed({ middleCode, siteCodes: null, siteId: "" });

        const response = await harness.post("/api/datahub/license-assertion", {
            token: harness.signToken()
        });

        assert.equal(response.status, 503, `middleCode=${middleCode} must not be enrollable`);
        assert.equal(response.body.error, "ASSERTION_UNAVAILABLE");
    }
});

test("a real site code listed beside a placeholder still enrolls", async () => {
    // Dropping placeholders must not drop the licence's genuine sites with them.
    reseed({ siteCodes: ["0000", "hn01", "TBD", " sg02 "] });

    const response = await harness.post("/api/datahub/license-assertion", {
        token: harness.signToken()
    });

    assert.equal(response.status, 200);
    const payload = openAssertion(response.body.licenseAssertion, harness.datahubPublicKey);
    assert.deepEqual(payload.SiteCodes, ["HN01", "SG02"]);
});

// ---- expiresAt -----------------------------------------------------------

test("an unreadable expiresAt leaves the licence perpetual", async () => {
    // Documents the current behaviour rather than endorsing it: a value the
    // parser cannot read is indistinguishable from a v1 record with no expiry,
    // so the key never expires. The server logs LICENSE_EXPIRES_AT_UNPARSEABLE
    // so the typo is findable; changing the outcome would expire the v1 fleet.
    //
    // "16/10/2026" is the day-first shape an operator here would naturally type,
    // and it is exactly the one Date.parse rejects — "2026/10/16" parses fine.
    reseed({ expiresAt: "16/10/2026" });

    const response = await verify();

    assert.equal(response.status, 200);
    assert.equal(response.body.license.effectiveStatus, "active");
    assert.equal(response.body.license.expiresAt, null);
});

test("a well-formed expiresAt is echoed back with the +07:00 anchor", async () => {
    reseed({ expiresAt: "2099-10-16T00:00:00+07:00" });

    const response = await verify();

    assert.equal(response.status, 200);
    assert.equal(response.body.license.expiresAt, "2099-10-16T00:00:00+07:00");
    assert.equal(response.body.license.effectiveStatus, "active");
});

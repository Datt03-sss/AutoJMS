"use strict";

// ==========================================================================
// The tracked template — backend/firebase/config-key.example.json — run against
// the real server.
// ==========================================================================
// That file is what the owner pastes into /Licenses/{licenseKey}, so its default
// values ARE a control, not decoration: an unedited paste must not open the app,
// and must not borrow another customer's DataHub tenant. Both properties are
// spread across two files (the template's values, and the gates in server.js),
// which is the shape that drifts silently — nothing errors when a default stops
// lining up with the gate that made it safe.
//
// The last test is the other half of the pair. "Fails closed" is worthless on its
// own: a record that is broken for some unrelated reason also fails closed. So the
// same template, with only the two placeholders replaced, must verify AND carry
// every operational value through to the response.
// ==========================================================================

const test = require("node:test");
const assert = require("node:assert/strict");
const crypto = require("node:crypto");
const fs = require("node:fs");
const path = require("node:path");

const { FIXTURE, startServer, activeSession } = require("./helpers/harness");
const { computeExpiry } = require("../license-expiry");

const TEMPLATE_PATH = path.join(__dirname, "..", "..", "firebase", "config-key.example.json");
const RAW_TEMPLATE = fs.readFileSync(TEMPLATE_PATH, "utf8");

/** A fresh copy per test — the fake database keeps the object by reference. */
const template = () => JSON.parse(RAW_TEMPLATE);

/** Anchored to the 16th like a real key, and always at least 30 days out. */
const liveExpiry = () => computeExpiry(new Date().toISOString().slice(0, 10)).expiresAt;

const seedWith = record => ({
    Licenses: { [FIXTURE.licenseKey]: record },
    sessions: { [FIXTURE.sessionId]: activeSession() }
});

let harness;

test.before(async () => {
    harness = await startServer({ seed: seedWith(template()) });
});

test.after(async () => {
    await harness.close();
});

const verify = () =>
    harness.post("/api/verify-license", {
        body: { licenseKey: FIXTURE.licenseKey, hwid: FIXTURE.hwid }
    });

/** Reads the signed SiteCodes the way RsaLicenseAssertionValidator on the VPS does. */
const signedSiteCodes = assertion => {
    const [prefix, encodedPayload, signature] = String(assertion).split(".");
    assert.equal(prefix, "v1rs256");
    assert.ok(
        crypto.verify(
            "sha256",
            Buffer.from(encodedPayload, "utf8"),
            { key: harness.datahubPublicKey, padding: crypto.constants.RSA_PKCS1_PADDING },
            Buffer.from(signature, "base64url")
        ),
        "signature must verify with the public key the VPS holds"
    );
    return JSON.parse(Buffer.from(encodedPayload, "base64url").toString("utf8")).SiteCodes;
};

/** A live record built from the template, with the station code filled in as an owner would. */
const liveRecord = (middleCode, extra = {}) => {
    const record = { ...template(), middleCode, siteCodes: [middleCode], expiresAt: liveExpiry(), ...extra };
    delete record.meta.template;
    return record;
};

test("the template is strict JSON with no byte-order mark", () => {
    // A BOM is invisible in an editor and breaks JSON.parse here, in jq, and in
    // the Firebase console's paste box.
    assert.ok(!RAW_TEMPLATE.startsWith("\uFEFF"), "template must not start with a BOM");
    assert.doesNotThrow(() => JSON.parse(RAW_TEMPLATE));
});

test("pasted unedited, the template does not open the app", async () => {
    harness.db.reset(seedWith(template()));

    const response = await verify();

    assert.equal(response.status, 403);
    assert.equal(response.body.error, "LICENSE_EXPIRED");
});

test("its placeholder site code is refused a DataHub assertion even once the licence is live", async () => {
    harness.db.reset(seedWith({ ...template(), expiresAt: liveExpiry() }));

    const response = await verify();

    // Signing in is allowed — REQUIRE_UNIQUE_SITE_CODE is unset in this harness,
    // matching production. What must NOT happen is enrollment: no assertion means
    // the DataHub API turns the device away instead of seating it in a shared tenant.
    assert.equal(response.status, 200);
    assert.equal(response.body.datahub.licenseAssertion, "");
    assert.equal(response.body.datahub.assertionExpiresAt, 0);
});

test("with both placeholders replaced, every value in the template reaches the client", async () => {
    const record = {
        ...template(),
        middleCode: FIXTURE.siteCode,
        siteCodes: [FIXTURE.siteCode],
        expiresAt: liveExpiry()
    };
    delete record.meta.template;

    harness.db.reset(seedWith(record));

    const response = await verify();

    assert.equal(response.status, 200);

    // Identity and enrollment.
    assert.equal(response.body.tier, "ULTRA");
    assert.equal(response.body.license.effectiveStatus, "active");
    assert.equal(response.body.datahub.siteCode, FIXTURE.siteCode);
    assert.match(response.body.datahub.licenseAssertion, /^v1rs256\.[^.]+\.[^.]+$/);
    assert.ok(response.body.datahub.assertionExpiresAt > 0);

    // Operational values. Each one is a field the template sets and the server
    // reads; a typo in either file lands here rather than in the field.
    assert.equal(response.body.skipHashCheck, true);
    assert.equal(response.body.license.graceDays, 7);
    assert.equal(response.body.license.offlineGraceHours, 72);
    assert.equal(response.body.license.seats, 3);
    assert.equal(response.body.cfg.updateChannel, "stable");
    assert.deepEqual(response.body.modulePolicy, {
        autoUpdate: false,
        silentUpdate: true,
        applyOnNextStartup: true
    });
});

// ---- middleCode ≡ siteCode ----------------------------------------------
// One station identifier, two names: middleCode is what the WinForms app calls
// it, siteCode is what DataHub and PostgreSQL call it. The template asks the
// owner to write that one value into two fields, so the tests below pin the
// consequence — every name the response uses for it carries the same value, and
// the signed scope agrees with the field the client actually sends to enroll.
//
// This is not cosmetic. EnrollmentEndpoints.cs compares
// request.SiteCode.Trim().ToUpperInvariant() against the signed SiteCodes with an
// ORDINAL comparer, so the two sides match only while every layer normalises the
// same way. Nothing above this file would fail if one of them stopped.

test("one station code, every name: the value the owner types reaches all of them", async () => {
    harness.db.reset(seedWith(liveRecord(FIXTURE.siteCode)));

    const body = (await verify()).body;

    // App-facing name, DataHub-facing name, and the signed scope: one value.
    assert.equal(body.middleCode, FIXTURE.siteCode);
    assert.equal(body.license.middleCode, FIXTURE.siteCode);
    assert.equal(body.license.siteCode, FIXTURE.siteCode);
    assert.equal(body.datahub.siteCode, FIXTURE.siteCode);
    assert.deepEqual(signedSiteCodes(body.datahub.licenseAssertion), [FIXTURE.siteCode]);
});

test("a lower-case middleCode is upper-cased on both sides of the enrollment match", async () => {
    const typed = FIXTURE.siteCode.toLowerCase();
    assert.notEqual(typed, FIXTURE.siteCode, "fixture must have letters for this test to mean anything");

    harness.db.reset(seedWith(liveRecord(typed)));

    const body = (await verify()).body;

    // The client sends datahub.siteCode (or middleCode) upper-cased; the VPS looks
    // it up in the signed list with an ordinal comparer. Both of the values this
    // server controls must therefore already be upper-cased — a record typed in
    // lower case still enrolls, and stays in the same tenant as before.
    assert.equal(body.datahub.siteCode, FIXTURE.siteCode);
    assert.deepEqual(signedSiteCodes(body.datahub.licenseAssertion), [FIXTURE.siteCode]);
    assert.equal(body.middleCode, typed, "middleCode is echoed verbatim; the client normalises it");
});

test("siteCodes may be omitted — middleCode alone is a complete site declaration", async () => {
    // The middleCode → siteCode fallback is by design, not a patch for old records:
    // the two fields hold the same value, so a key that declares only middleCode is
    // fully specified. Deleting this branch would strand every single-branch key.
    const record = liveRecord(FIXTURE.siteCode);
    delete record.siteCodes;

    harness.db.reset(seedWith(record));

    const withFallback = (await verify()).body;
    assert.equal(withFallback.datahub.siteCode, FIXTURE.siteCode);
    assert.deepEqual(signedSiteCodes(withFallback.datahub.licenseAssertion), [FIXTURE.siteCode]);

    // An EMPTY array is the opposite claim — "this licence covers no site" — and it
    // wins over the fallback. Absent and empty must not behave alike.
    harness.db.reset(seedWith(liveRecord(FIXTURE.siteCode, { siteCodes: [] })));

    const withEmptyList = (await verify()).body;
    assert.equal(withEmptyList.datahub.licenseAssertion, "");
    assert.equal(withEmptyList.datahub.assertionExpiresAt, 0);
});

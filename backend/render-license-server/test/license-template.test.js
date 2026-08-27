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

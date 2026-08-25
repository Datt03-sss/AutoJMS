"use strict";

// verify-license is the launch gate: it decides tier, mints the access token, and
// hands out the DataHub enrollment assertion. This file covers the contract the
// desktop client parses — it reads these fields by name, so a rename here is a
// silent feature loss there. The input guards live in
// verify-license-guards.test.js.
//
// Why two files: the route is rate limited to 20 requests/minute per IP, and
// `node --test` runs each FILE in its own process, so each file gets its own
// budget. Splitting is cheaper than raising a production limit for tests.

const test = require("node:test");
const assert = require("node:assert/strict");
const crypto = require("node:crypto");

const { FIXTURE, startServer, activeLicense, activeSession, daysAgo } = require("./helpers/harness");

let harness;

test.before(async () => {
    harness = await startServer();
});

test.after(async () => {
    await harness.close();
});

const seed = (licenseOverrides = {}, extra = {}) =>
    harness.db.reset({
        Licenses: { [FIXTURE.licenseKey]: activeLicense(licenseOverrides) },
        ...extra
    });

const verify = (body = {}) =>
    harness.post("/api/verify-license", {
        body: { licenseKey: FIXTURE.licenseKey, hwid: FIXTURE.hwid, ...body }
    });

/** The one session the route created, whatever random id it was given. */
const onlySession = () => {
    const sessions = harness.db.read("sessions") || {};
    const ids = Object.keys(sessions);
    assert.equal(ids.length, 1, `expected exactly one session, found ${ids.length}`);
    return { id: ids[0], value: sessions[ids[0]] };
};

test("an active license activates and returns the full client contract", async () => {
    seed();

    const response = await verify();

    assert.equal(response.status, 200);
    assert.equal(response.body.tier, "ULTRA");
    assert.equal(response.body.middleCode, FIXTURE.siteCode);

    const claims = harness.verifyJwt(response.body.payload);
    assert.equal(claims.key, FIXTURE.licenseKey);
    assert.equal(claims.tier, "ULTRA");
    assert.equal(claims.sid, response.body.sid);

    // The lifecycle block. `expiresAt: null` means "no expiry known", never
    // "expired" — the client must not treat the two the same.
    assert.equal(response.body.license.effectiveStatus, "active");
    assert.equal(response.body.license.expiresAt, null);
    assert.equal(response.body.license.siteCode, FIXTURE.siteCode);
    assert.equal(response.body.license.offlineGraceHours, 72);
    assert.equal(response.body.license.billingAnchorDay, 16);
    assert.equal(response.body.license.graceDays, 7);
    assert.equal(response.body.license.seats, 3);

    assert.equal(response.body.cfg.dataSpreadsheetId, FIXTURE.spreadsheetId);
    assert.equal(response.body.cfg.updateChannel, "stable");

    // Program.cs gates every DataHub service on apiBaseUrl, and
    // MajorUpdateServiceInstance additionally on manifests — an empty block here
    // silently disables updates, runtime policy, and integrity checks.
    assert.equal(response.body.datahub.apiBaseUrl, FIXTURE.datahubUrl);
    assert.equal(response.body.datahub.siteCode, FIXTURE.siteCode);
    assert.equal(response.body.datahub.manifests.versionLatest, "manifest/version-latest.json");
    assert.equal(response.body.datahub.manifests.tierDefinitions, "manifest/tier-definitions.json");

    // Two manifest keys sharing one path is always a copy-paste, never a design:
    // `smallUpdateManifest` used to duplicate `selectorUpdateManifest` exactly.
    // The client drops keys its model does not declare, so the duplicate was
    // invisible from both ends until someone diffed the object by hand.
    const paths = Object.values(response.body.datahub.manifests);
    assert.equal(new Set(paths).size, paths.length, "manifest paths must be distinct");

    const session = onlySession();
    assert.equal(session.value.status, "active");
    assert.equal(session.value.tier, "ULTRA");
    assert.equal(session.id, response.body.sid);
});

test("the DataHub assertion is a verifiable v1rs256 token with PascalCase claims", async () => {
    seed({ seats: 12, tokenVersion: 4 });

    const response = await verify();
    const [prefix, encodedPayload, signature] = response.body.datahub.licenseAssertion.split(".");

    assert.equal(prefix, "v1rs256");

    const verified = crypto.verify(
        "sha256",
        Buffer.from(encodedPayload, "utf8"),
        { key: harness.datahubPublicKey, padding: crypto.constants.RSA_PKCS1_PADDING },
        Buffer.from(signature, "base64url")
    );
    assert.ok(verified, "the VPS validates this signature with the matching public key");

    // PascalCase is required: RsaLicenseAssertionValidator deserializes into
    // LicenseAssertionPayload with System.Text.Json defaults, which are
    // case-sensitive. camelCase produces an empty payload and a 401 that says
    // nothing about why.
    const payload = JSON.parse(Buffer.from(encodedPayload, "base64url").toString("utf8"));
    assert.deepEqual(Object.keys(payload).sort(), [
        "Audience",
        "Channel",
        "DataHubUrl",
        "ExpiresAt",
        "Issuer",
        "Seats",
        "SiteCodes",
        "TokenVersion"
    ]);
    assert.deepEqual(payload.SiteCodes, [FIXTURE.siteCode]);
    assert.equal(payload.Channel, FIXTURE.channel);
    assert.equal(payload.Issuer, FIXTURE.assertionIssuer);
    assert.equal(payload.Audience, FIXTURE.assertionAudience);
    assert.equal(payload.DataHubUrl, FIXTURE.datahubUrl);
    assert.equal(payload.Seats, 12);
    assert.equal(payload.TokenVersion, 4);
    assert.ok(payload.ExpiresAt > Math.floor(Date.now() / 1000));
    assert.equal(response.body.datahub.assertionExpiresAt, payload.ExpiresAt);
});

test("an unknown license key is a 404", async () => {
    harness.db.reset({});

    const response = await verify();

    assert.equal(response.status, 404);
    assert.equal(response.body.error, "LICENSE_NOT_FOUND");
});

test("a locked license is refused", async () => {
    seed({ status: "locked" });

    const response = await verify();

    assert.equal(response.status, 401);
    assert.equal(response.body.error, "LICENSE_INACTIVE");
});

test("an expired license is refused and no session is created", async () => {
    seed({ expiresAt: daysAgo(40) });

    const response = await verify();

    assert.equal(response.status, 403);
    assert.equal(response.body.error, "LICENSE_EXPIRED");
    assert.ok(response.body.expiresAt);
    assert.equal(harness.db.read("sessions"), null);
});

test("a license in grace still launches, and the client is told", async () => {
    seed({ expiresAt: daysAgo(2) });

    const response = await verify();

    assert.equal(response.status, 200);
    assert.equal(response.body.license.effectiveStatus, "grace");
    assert.ok(response.body.license.graceUntil);
});

test("an unrecognised tier is refused rather than silently downgraded", async () => {
    // "PRO" or "Ultra " used to pass through untouched and land the station on the
    // BASE entitlement set — a downgrade nobody notices until a customer reports a
    // missing feature.
    seed({ tier: "PRO" });

    const response = await verify();

    assert.equal(response.status, 403);
    assert.equal(response.body.error, "LICENSE_TIER_INVALID");
});

test("a lowercase tier is normalised rather than rejected", async () => {
    seed({ tier: " ultra " });

    const response = await verify();

    assert.equal(response.status, 200);
    assert.equal(response.body.tier, "ULTRA");
});

test("a first activation binds the HWID and stamps activatedAt", async () => {
    seed({ hwid: undefined });

    const response = await verify();

    assert.equal(response.status, 200);
    const record = harness.db.read(`Licenses/${FIXTURE.licenseKey}`);
    assert.equal(record.hwid, FIXTURE.hwid);
    // ISO with an explicit +07:00 offset, so a human reading the Firebase console
    // sees a date rather than an epoch number.
    assert.match(record.activatedAt, /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\+07:00$/);
});

test("a license bound to another machine is refused", async () => {
    seed({ hwid: "ffffffffffffffffffffffffffffffff" });

    const response = await verify();

    assert.equal(response.status, 401);
    assert.equal(response.body.error, "HWID_MISMATCH");
});

test("relaunching the same machine replaces its previous session", async () => {
    seed({}, {
        sessions: {
            "old-session-same-device": activeSession(),
            "other-device": activeSession({ hwid: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" })
        }
    });

    const response = await verify();

    assert.equal(response.status, 200);
    const sessions = harness.db.read("sessions");
    // The stale session for THIS device is gone; another device's stays.
    assert.equal(sessions["old-session-same-device"], undefined);
    assert.ok(sessions["other-device"]);
    assert.ok(sessions[response.body.sid]);
});

test("with no signing key the response still launches the app but cannot enroll", async () => {
    // A deployment that forgot DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY must not be
    // able to enroll devices — but it must also not refuse to start stations.
    const degraded = await startServer({
        withAssertionKey: false,
        seed: { Licenses: { [FIXTURE.licenseKey]: activeLicense() } }
    });

    try {
        const response = await degraded.post("/api/verify-license", {
            body: { licenseKey: FIXTURE.licenseKey, hwid: FIXTURE.hwid }
        });

        assert.equal(response.status, 200);
        assert.equal(response.body.datahub.licenseAssertion, "");
        assert.equal(response.body.datahub.assertionExpiresAt, 0);
        // Still populated: the manifest reads are anonymous and keep working.
        assert.equal(response.body.datahub.apiBaseUrl, FIXTURE.datahubUrl);
    } finally {
        await degraded.close();
    }
});

test("a placeholder site code is refused when enforcement is on", async () => {
    // Every key in the fleet still ships middleCode "0000", so enforcement is
    // opt-in until they are migrated. Both halves of that switch are asserted.
    const strict = await startServer({
        env: { REQUIRE_UNIQUE_SITE_CODE: "1" },
        seed: { Licenses: { [FIXTURE.licenseKey]: activeLicense({ middleCode: "0000" }) } }
    });

    try {
        const refused = await strict.post("/api/verify-license", {
            body: { licenseKey: FIXTURE.licenseKey, hwid: FIXTURE.hwid }
        });

        assert.equal(refused.status, 403);
        assert.equal(refused.body.error, "LICENSE_SITE_CODE_INVALID");

        strict.db.reset({ Licenses: { [FIXTURE.licenseKey]: activeLicense({ middleCode: "HN07" }) } });
        const allowed = await strict.post("/api/verify-license", {
            body: { licenseKey: FIXTURE.licenseKey, hwid: FIXTURE.hwid }
        });

        assert.equal(allowed.status, 200);
        assert.equal(allowed.body.license.siteCode, "HN07");
    } finally {
        await strict.close();
    }
});

test("a placeholder site code is allowed while enforcement is off", async () => {
    seed({ middleCode: "0000" });

    const response = await verify();

    assert.equal(response.status, 200);
    assert.equal(response.body.license.siteCode, "0000");
    // No site means no assertion: "cannot enroll", never "enroll unrestricted".
    assert.equal(response.body.datahub.siteCode, "0000");
});

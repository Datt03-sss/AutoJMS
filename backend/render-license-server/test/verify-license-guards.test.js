"use strict";

// The input guards on verify-license. `Licenses/${licenseKey}` puts the caller's
// string straight into a Realtime Database path, so these run BEFORE the read —
// and the tests assert that by checking the database was never touched.
//
// The guard is deliberately "cannot break the path" and not a format check: the
// fleet's key format is recorded nowhere, so a stricter pattern would lock out
// live keys. That is why the accepted-shapes test below is as important as the
// rejected ones.

const test = require("node:test");
const assert = require("node:assert/strict");

const { FIXTURE, startServer, activeLicense } = require("./helpers/harness");

let harness;

test.before(async () => {
    harness = await startServer();
});

test.after(async () => {
    await harness.close();
});

const reseed = () =>
    harness.db.reset({ Licenses: { [FIXTURE.licenseKey]: activeLicense() } });

const verify = (body = {}) =>
    harness.post("/api/verify-license", {
        body: { licenseKey: FIXTURE.licenseKey, hwid: FIXTURE.hwid, ...body }
    });

test("a license key containing a slash is refused before any Firebase read", async () => {
    reseed();

    const response = await verify({ licenseKey: "AJMS/../Licenses" });

    assert.equal(response.status, 400);
    assert.equal(response.body.error, "LICENSE_KEY_INVALID");
    // The point of the guard: the slash would otherwise walk to a different node.
    assert.deepEqual(harness.db.reads(), []);
});

test("Firebase-illegal characters in the key are refused before any read", async () => {
    // A dot, hash, dollar or bracket throws inside ref() itself, which used to
    // surface as a 500 that explained nothing.
    for (const licenseKey of ["a.b.c.d", "abc#def", "abc$def", "ab[cd]", "ab]cd"]) {
        reseed();

        const response = await verify({ licenseKey });

        assert.equal(response.status, 400, `expected 400 for ${licenseKey}`);
        assert.equal(response.body.error, "LICENSE_KEY_INVALID");
        assert.deepEqual(harness.db.reads(), [], `${licenseKey} must not reach ref()`);
    }
});

test("a key that is too short or too long is refused", async () => {
    for (const licenseKey of ["ab", "x".repeat(129)]) {
        reseed();

        const response = await verify({ licenseKey });

        assert.equal(response.status, 400, `expected 400 for a ${licenseKey.length}-char key`);
        assert.equal(response.body.error, "LICENSE_KEY_INVALID");
    }
});

test("ordinary key shapes in the fleet are still accepted", async () => {
    // The guard must not become a format check. These are the shapes a live key
    // plausibly has, and every one of them has to keep working.
    for (const licenseKey of ["AJMS-TEST-0001-KEY0", "abcd", "KEY with spaces", "khoá-tiếng-việt"]) {
        harness.db.reset({ Licenses: { [licenseKey]: activeLicense() } });

        const response = await harness.post("/api/verify-license", {
            body: { licenseKey, hwid: FIXTURE.hwid }
        });

        assert.equal(response.status, 200, `expected 200 for ${licenseKey}`);
    }
});

test("an implausibly short HWID is refused", async () => {
    reseed();

    const response = await verify({ hwid: "short" });

    assert.equal(response.status, 400);
    assert.equal(response.body.error, "HWID_INVALID");
    assert.deepEqual(harness.db.reads(), []);
});

test("an oversized HWID is refused", async () => {
    reseed();

    const response = await verify({ hwid: "a".repeat(257) });

    assert.equal(response.status, 400);
    assert.equal(response.body.error, "HWID_INVALID");
});

test("a missing field is refused before the format check", async () => {
    reseed();

    const response = await verify({ licenseKey: "" });

    // MISSING_REQUIRED_FIELDS rather than LICENSE_KEY_INVALID: an empty field is
    // a client bug, a malformed one is a rejected value, and the client shows
    // different messages for the two.
    assert.equal(response.status, 400);
    assert.equal(response.body.error, "MISSING_REQUIRED_FIELDS");
});

test("a non-string key is refused rather than coerced", async () => {
    reseed();

    const response = await verify({ licenseKey: { $ref: "Licenses" } });

    assert.equal(response.status, 400);
    assert.equal(response.body.error, "LICENSE_KEY_INVALID");
    assert.deepEqual(harness.db.reads(), []);
});

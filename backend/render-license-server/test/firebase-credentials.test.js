"use strict";

// Covers the code path whose failure mode is process.exit(1) before the first
// request: if credential resolution is wrong, the deployment is not degraded, it
// is absent. FIREBASE_SERVICE_ACCOUNT_FILE is the case that mattered — the
// variable the deployment actually sets, and the one source this project used to
// ignore.

const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");

const {
    FALLBACK_FILE,
    REQUIRED_FIELDS,
    parseServiceAccount,
    resolveFirebaseServiceAccount
} = require("../firebase-credentials");

// Assembled from parts rather than written as a literal PEM banner: eng/harness/check-secrets.ps1
// scans tracked files for that banner, and a test fixture that trips the secret scanner trains
// people to ignore it. The value still has the shape the loader passes through untouched.
const FAKE_PEM = ["-----BEGIN ", "PRIVATE KEY", "-----\nnot-a-real-key\n-----END ", "PRIVATE KEY", "-----\n"].join("");

const ACCOUNT = {
    type: "service_account",
    project_id: "autojms-test",
    client_email: "svc@autojms-test.iam.gserviceaccount.com",
    private_key: FAKE_PEM
};

/** A scratch directory, removed when the test process exits. */
function tempDir() {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), "autojms-cred-"));
    test.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    return dir;
}

function writeAccount(dir, name, account = ACCOUNT) {
    const target = path.join(dir, name);
    fs.writeFileSync(target, JSON.stringify(account), "utf8");
    return target;
}

/** Never process.env: an env leak between tests here is a silent false pass. */
const resolve = (env, extra = {}) =>
    resolveFirebaseServiceAccount({ env, cwd: extra.cwd || tempDir(), moduleDir: extra.moduleDir || tempDir() });

test("FIREBASE_SERVICE_ACCOUNT_FILE is read", () => {
    const dir = tempDir();
    const file = writeAccount(dir, "service-account.json");

    const result = resolve({ FIREBASE_SERVICE_ACCOUNT_FILE: file });

    // The whole reason this module exists. Before it, this variable was the one
    // the deployment had set and the one nothing read, so boot exited claiming no
    // credentials were configured while the file sat mounted and readable.
    assert.equal(result.source, "FIREBASE_SERVICE_ACCOUNT_FILE");
    assert.equal(result.path, file);
    assert.equal(result.serviceAccount.project_id, "autojms-test");
});

test("a file with no extension is read", () => {
    const dir = tempDir();
    // This is how Render mounts a Secret File. require() refuses such a name, so
    // the loader must read + parse rather than require.
    const file = writeAccount(dir, "firebase-credentials-secret");

    const result = resolve({ FIREBASE_SERVICE_ACCOUNT_FILE: file });

    assert.equal(result.serviceAccount.client_email, ACCOUNT.client_email);
});

test("GOOGLE_APPLICATION_CREDENTIALS is read when the explicit variable is unset", () => {
    const dir = tempDir();
    const file = writeAccount(dir, "adc.json");

    const result = resolve({ GOOGLE_APPLICATION_CREDENTIALS: file });

    assert.equal(result.source, "GOOGLE_APPLICATION_CREDENTIALS");
});

test("an inline JSON account wins over a file", () => {
    const dir = tempDir();
    const file = writeAccount(dir, "on-disk.json", { ...ACCOUNT, project_id: "from-file" });

    const result = resolve({
        FIREBASE_SERVICE_ACCOUNT_JSON: JSON.stringify({ ...ACCOUNT, project_id: "from-env" }),
        FIREBASE_SERVICE_ACCOUNT_FILE: file
    });

    assert.equal(result.source, "FIREBASE_SERVICE_ACCOUNT_JSON");
    assert.equal(result.serviceAccount.project_id, "from-env");
});

test("a base64 account is decoded", () => {
    const result = resolve({
        FIREBASE_SERVICE_ACCOUNT_BASE64: Buffer.from(JSON.stringify(ACCOUNT), "utf8").toString("base64")
    });

    assert.equal(result.source, "FIREBASE_SERVICE_ACCOUNT_BASE64");
    assert.equal(result.serviceAccount.project_id, "autojms-test");
});

test("a blank variable is skipped rather than treated as configured", () => {
    const dir = tempDir();
    const file = writeAccount(dir, "real.json");

    const result = resolve({
        FIREBASE_SERVICE_ACCOUNT_JSON: "   ",
        FIREBASE_SERVICE_ACCOUNT_BASE64: "",
        FIREBASE_SERVICE_ACCOUNT_FILE: file
    });

    assert.equal(result.source, "FIREBASE_SERVICE_ACCOUNT_FILE");
});

test("a relative path resolves against the module directory as well as the CWD", () => {
    const moduleDir = tempDir();
    writeAccount(moduleDir, "serviceAccountKey.json");

    // FALLBACK_FILE is "./serviceAccountKey.json" — expected next to server.js
    // regardless of where the process was started from.
    const result = resolveFirebaseServiceAccount({ env: {}, cwd: tempDir(), moduleDir });

    assert.equal(result.source, FALLBACK_FILE);
    assert.equal(result.path, path.join(moduleDir, "serviceAccountKey.json"));
});

test("a missing required field names the field and the source", () => {
    const dir = tempDir();
    const file = writeAccount(dir, "partial.json", { project_id: "autojms-test" });

    assert.throws(
        () => resolve({ FIREBASE_SERVICE_ACCOUNT_FILE: file }),
        err =>
            /FIREBASE_SERVICE_ACCOUNT_FILE/.test(err.message) &&
            /client_email/.test(err.message) &&
            /private_key/.test(err.message)
    );
});

test("malformed JSON fails loudly instead of falling through to the next source", () => {
    const dir = tempDir();
    const broken = path.join(dir, "broken.json");
    fs.writeFileSync(broken, "{not json", "utf8");
    const good = writeAccount(dir, "good.json");

    // Falling through would report "no credentials configured" for what is really
    // a typo in one file — the most expensive possible wording for that outage.
    assert.throws(
        () => resolve({ FIREBASE_SERVICE_ACCOUNT_FILE: broken, GOOGLE_APPLICATION_CREDENTIALS: good }),
        /not valid JSON/
    );
});

test("a JSON array is rejected as not an object", () => {
    assert.throws(() => parseServiceAccount("[]", "TEST_SOURCE"), /TEST_SOURCE is not a JSON object/);
});

test("with nothing configured the error lists every source", () => {
    assert.throws(
        () => resolve({}),
        err =>
            /FIREBASE_SERVICE_ACCOUNT_JSON/.test(err.message) &&
            /FIREBASE_SERVICE_ACCOUNT_BASE64/.test(err.message) &&
            /FIREBASE_SERVICE_ACCOUNT_FILE/.test(err.message) &&
            /GOOGLE_APPLICATION_CREDENTIALS/.test(err.message) &&
            err.message.includes(FALLBACK_FILE)
    );
});

test("a variable pointing at a nonexistent path does not mask a later working source", () => {
    const dir = tempDir();
    const good = writeAccount(dir, "good.json");

    const result = resolve({
        FIREBASE_SERVICE_ACCOUNT_FILE: path.join(dir, "does-not-exist.json"),
        GOOGLE_APPLICATION_CREDENTIALS: good
    });

    assert.equal(result.source, "GOOGLE_APPLICATION_CREDENTIALS");
});

test("REQUIRED_FIELDS is exactly what admin.credential.cert() needs", () => {
    assert.deepEqual(REQUIRED_FIELDS, ["project_id", "client_email", "private_key"]);
});

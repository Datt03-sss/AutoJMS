"use strict";

// ==========================================================================
// Boots server.js in-process against fakes, and hands back a live base URL.
// ==========================================================================
// server.js does its work in its module body: it exits the process if the JWT
// keys are missing, resolves the Firebase credential, calls initializeApp, and
// builds the router. So every fake has to be in require.cache and every env var
// set BEFORE the require — which is why this is a function that requires, rather
// than a module that imports.
//
// One consequence worth knowing: `node --test` runs each test FILE in its own
// process, so each file gets its own module cache and its own rate-limiter
// budget. Within a file, the server is booted once and the database is reset
// between tests.
// ==========================================================================

const Module = require("module");
const crypto = require("crypto");
const fs = require("fs");
const os = require("os");
const path = require("path");

const { createFakeFirebase } = require("./fake-firebase");

const ISSUER = "autojms-license-server";
const AUDIENCE = "autojms-desktop-client";

/** Values the tests assert against, so a change here shows up as a test change. */
const FIXTURE = {
    issuer: ISSUER,
    audience: AUDIENCE,
    datahubUrl: "https://datahub.test.local",
    assertionIssuer: "autojms-license-test",
    assertionAudience: "autojms-datahub-enroll-test",
    channel: "test",
    licenseKey: "AJMS-TEST-0001-KEY0",
    hwid: "0123456789abcdef0123456789abcdef",
    sessionId: "11111111-2222-3333-4444-555555555555",
    siteCode: "HN01",
    spreadsheetId: "sheet-abc-123",
    googleAccessToken: "ya29.fake-access-token"
};

function rsaPair() {
    return crypto.generateKeyPairSync("rsa", {
        modulusLength: 2048,
        publicKeyEncoding: { type: "spki", format: "pem" },
        privateKeyEncoding: { type: "pkcs8", format: "pem" }
    });
}

/**
 * Places a ready-made module in require.cache so server.js's require() finds it
 * instead of the real package. A real Module instance is used rather than a bare
 * object because Node's loader reads `.exports` off the cached Module and would
 * reject a plain object.
 */
function injectModule(request, exports) {
    const resolved = require.resolve(request);
    const stub = new Module(resolved, null);
    stub.filename = resolved;
    stub.path = path.dirname(resolved);
    stub.loaded = true;
    stub.exports = exports;
    require.cache[resolved] = stub;
    return resolved;
}

/**
 * A stand-in for google-auth-library's GoogleAuth. The broker route only needs a
 * client with getAccessToken() and a credentials.expiry_date, and the tests need
 * to be able to make both fail.
 */
function createFakeGoogleAuth() {
    const state = {
        token: FIXTURE.googleAccessToken,
        expiryDate: 0,
        getClientError: null,
        getAccessTokenError: null,
        accessTokenCalls: 0
    };

    class FakeGoogleAuth {
        constructor(options) {
            state.lastOptions = options;
        }

        async getClient() {
            if (state.getClientError) throw new Error(state.getClientError);
            return {
                get credentials() {
                    return { expiry_date: state.expiryDate };
                },
                async getAccessToken() {
                    state.accessTokenCalls += 1;
                    if (state.getAccessTokenError) throw new Error(state.getAccessTokenError);
                    return { token: state.token };
                }
            };
        }
    }

    return {
        exports: { GoogleAuth: FakeGoogleAuth },
        state,
        /** How many access tokens have been minted — 0 proves a route refused before brokering. */
        accessTokenCalls: () => state.accessTokenCalls,
        resetCalls() {
            state.accessTokenCalls = 0;
        }
    };
}

/**
 * Boots the server.
 *
 * @param {object} [options]
 * @param {object} [options.seed] initial Firebase contents
 * @param {object} [options.env] extra environment, applied before the require
 * @param {boolean} [options.withAssertionKey=true] set the DataHub signing key
 * @returns {Promise<object>} the harness
 */
async function startServer(options = {}) {
    const seed = options.seed || {};
    const withAssertionKey = options.withAssertionKey !== false;

    const jwtKeys = rsaPair();
    const assertionKeys = rsaPair();

    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "autojms-license-test-"));
    // Deliberately extension-less: a Render Secret File is mounted under whatever
    // name it was given, and require() would refuse this one. If server.js ever
    // goes back to require()-ing the credential, this file breaks the boot.
    const serviceAccountPath = path.join(tempDir, "firebase-service-account");
    fs.writeFileSync(
        serviceAccountPath,
        JSON.stringify({
            type: "service_account",
            project_id: "autojms-test",
            client_email: "test@autojms-test.iam.gserviceaccount.com",
            private_key: assertionKeys.privateKey
        }),
        "utf8"
    );

    // EVERY variable server.js reads is listed, including the ones whose intended
    // value is "unset". A test that boots a second server in the same process
    // would otherwise inherit whatever the first one set — and a leaked
    // DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY makes the "enrollment is closed" test
    // pass for the wrong reason, which is the failure this list prevents.
    const env = {
        JWT_PRIVATE_KEY: jwtKeys.privateKey,
        JWT_PUBLIC_KEY: jwtKeys.publicKey,
        FIREBASE_SERVICE_ACCOUNT_FILE: serviceAccountPath,
        FIREBASE_SERVICE_ACCOUNT_JSON: undefined,
        FIREBASE_SERVICE_ACCOUNT_BASE64: undefined,
        GOOGLE_APPLICATION_CREDENTIALS: undefined,
        GOOGLE_SHEETS_SERVICE_ACCOUNT_FILE: serviceAccountPath,
        FIREBASE_DATABASE_URL: "https://autojms-test.firebaseio.test/",
        FIREBASE_OPERATION_TIMEOUT_MS: undefined,
        GOOGLE_SHEETS_GRANT_TIMEOUT_MS: undefined,
        DATAHUB_API_BASE_URL: FIXTURE.datahubUrl,
        // Inlined rather than read from a .env file: dotenv.config() does not
        // overwrite variables that are already set, so these win either way.
        DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY: withAssertionKey ? assertionKeys.privateKey : undefined,
        DATAHUB_LICENSE_ASSERTION_ISSUER: FIXTURE.assertionIssuer,
        DATAHUB_LICENSE_ASSERTION_AUDIENCE: FIXTURE.assertionAudience,
        DATAHUB_LICENSE_ASSERTION_TTL_SECONDS: undefined,
        DATAHUB_DEFAULT_SEATS: undefined,
        DATAHUB_CHANNEL: FIXTURE.channel,
        DEFAULT_UPDATE_CHANNEL: undefined,
        LICENSE_GRACE_DAYS: undefined,
        LICENSE_OFFLINE_GRACE_HOURS: undefined,
        LICENSE_BILLING_ANCHOR_DAY: undefined,
        REQUIRE_UNIQUE_SITE_CODE: undefined,
        VALID_EXE_HASHES: undefined,
        ...(options.env || {})
    };

    for (const [key, value] of Object.entries(env)) {
        if (value === undefined) delete process.env[key];
        else process.env[key] = String(value);
    }

    const firebase = createFakeFirebase(seed);
    const google = createFakeGoogleAuth();

    injectModule("firebase-admin", firebase.admin);
    injectModule("google-auth-library", google.exports);

    // Required last, and only now: everything above has to be in place first.
    const serverPath = require.resolve("../../server");
    delete require.cache[serverPath];
    const app = require("../../server");

    const jwt = require("jsonwebtoken");

    const server = await new Promise(resolve => {
        const listening = app.listen(0, "127.0.0.1", () => resolve(listening));
    });

    const { port } = server.address();
    const baseUrl = `http://127.0.0.1:${port}`;

    /** A token shaped exactly like the ones signAccessToken() issues. */
    const signToken = (claims = {}) => {
        const payload = {
            key: FIXTURE.licenseKey,
            hwid: FIXTURE.hwid,
            sid: FIXTURE.sessionId,
            tier: "ULTRA",
            jti: crypto.randomUUID(),
            ...claims
        };

        // A test that needs a token with no jti passes jti: null; jsonwebtoken
        // rejects a null claim, so drop the property instead.
        if (payload.jti === null || payload.jti === undefined) delete payload.jti;

        return jwt.sign(payload, jwtKeys.privateKey, {
            algorithm: "RS256",
            expiresIn: claims.expiresIn || "60m",
            issuer: claims.issuer || ISSUER,
            audience: claims.audience || AUDIENCE,
            keyid: "accessKey"
        });
    };

    const request = async (method, routePath, { body, token, headers } = {}) => {
        const response = await fetch(baseUrl + routePath, {
            method,
            headers: {
                ...(body === undefined ? {} : { "content-type": "application/json" }),
                ...(token ? { authorization: `Bearer ${token}` } : {}),
                ...headers
            },
            body: body === undefined ? undefined : JSON.stringify(body)
        });

        const text = await response.text();
        let json = null;
        try {
            json = text ? JSON.parse(text) : null;
        } catch {
            json = null;
        }

        return { status: response.status, body: json, text, headers: response.headers };
    };

    return {
        baseUrl,
        app,
        db: firebase,
        google,
        jwtPublicKey: jwtKeys.publicKey,
        datahubPublicKey: assertionKeys.publicKey,
        signToken,
        request,
        get: (routePath, opts) => request("GET", routePath, opts),
        post: (routePath, opts) => request("POST", routePath, opts),
        verifyJwt: token =>
            jwt.verify(token, jwtKeys.publicKey, {
                algorithms: ["RS256"],
                issuer: ISSUER,
                audience: AUDIENCE
            }),
        async close() {
            await new Promise(resolve => server.close(resolve));
            try {
                fs.rmSync(tempDir, { recursive: true, force: true });
            } catch {
                // A leftover temp directory does not affect the next run.
            }
        }
    };
}

/** An active, perpetual ULTRA license — the baseline every test mutates from. */
function activeLicense(overrides = {}) {
    return {
        status: "active",
        tier: "ULTRA",
        hwid: FIXTURE.hwid,
        middleCode: FIXTURE.siteCode,
        dataSpreadsheetId: FIXTURE.spreadsheetId,
        skipHashCheck: true,
        ...overrides
    };
}

/** An active session matching the token signToken() produces by default. */
function activeSession(overrides = {}) {
    return {
        licenseKey: FIXTURE.licenseKey,
        hwid: FIXTURE.hwid,
        tier: "ULTRA",
        middleCode: FIXTURE.siteCode,
        status: "active",
        createdAt: 1_700_000_000_000,
        lastPing: 1_700_000_000_000,
        ...overrides
    };
}

/** A whole database with one active license and one live session. */
function seedWithActiveSession(licenseOverrides = {}, sessionOverrides = {}) {
    return {
        Licenses: { [FIXTURE.licenseKey]: activeLicense(licenseOverrides) },
        sessions: { [FIXTURE.sessionId]: activeSession(sessionOverrides) }
    };
}

const DAY_MS = 86_400_000;

/** An ISO instant `days` in the past (negative for the future). */
const daysAgo = days => new Date(Date.now() - days * DAY_MS).toISOString();

module.exports = {
    FIXTURE,
    DAY_MS,
    startServer,
    activeLicense,
    activeSession,
    seedWithActiveSession,
    daysAgo
};

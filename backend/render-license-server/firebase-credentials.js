"use strict";

// ==========================================================================
// Firebase Admin credential resolution
// ==========================================================================
// Four sources, tried in the order a deployment is most likely to use them.
//
// This lives in its own module for one reason: it runs exactly once, before the
// first request, and its failure mode is process.exit(1) — a total outage
// explained by a single console line. That is precisely the code that needs a
// test, and it cannot have one while it is welded into server.js's module body.
//
// FIREBASE_SERVICE_ACCOUNT_FILE is the important entry. It is the variable the
// deployed license server actually has set (server.js's Google Sheets broker
// already falls back to it, which is how we know), and it was the one source this
// project never read. Pointing a deployment at this directory would therefore
// exit(1) on boot with a message claiming no credentials were configured, while
// the credentials sat mounted and readable the whole time.
//
// Dependency-free and side-effect-free apart from reading the files it is told
// to read, so `node --test` can cover it without mocks.
// ==========================================================================

const fs = require("fs");
const path = require("path");

/** Every field admin.credential.cert() needs. Checked here so the error names its source. */
const REQUIRED_FIELDS = ["project_id", "client_email", "private_key"];

/** Env vars carrying the account inline, most explicit first. */
const INLINE_SOURCES = [
    { name: "FIREBASE_SERVICE_ACCOUNT_JSON", decode: value => value },
    { name: "FIREBASE_SERVICE_ACCOUNT_BASE64", decode: value => Buffer.from(value, "base64").toString("utf8") }
];

/** Env vars carrying a path to the account. */
const FILE_SOURCES = ["FIREBASE_SERVICE_ACCOUNT_FILE", "GOOGLE_APPLICATION_CREDENTIALS"];

/** Where a developer drops the file for local work. */
const FALLBACK_FILE = "./serviceAccountKey.json";

/**
 * Parses and validates one candidate. Throws rather than returning null: a source
 * that is present but unusable is a configuration mistake, and falling through to
 * the next source would hide it behind a "no credentials configured" message.
 */
function parseServiceAccount(text, source) {
    let parsed;

    try {
        parsed = JSON.parse(text);
    } catch (err) {
        throw new Error(`${source} is not valid JSON: ${err.message}`);
    }

    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
        throw new Error(`${source} is not a JSON object.`);
    }

    const missing = REQUIRED_FIELDS.filter(field => !parsed[field]);
    if (missing.length > 0) {
        throw new Error(`${source} is missing ${missing.join(", ")}.`);
    }

    return parsed;
}

/**
 * The first path that exists, or null.
 *
 * A relative path is tried against the process CWD first — that is how
 * GOOGLE_APPLICATION_CREDENTIALS is conventionally interpreted — and then against
 * this directory, which is where FALLBACK_FILE is expected to sit regardless of
 * where the process was started from.
 */
function resolveCandidatePath(filePath, { cwd, moduleDir }) {
    const candidates = path.isAbsolute(filePath)
        ? [filePath]
        : [path.resolve(cwd, filePath), path.resolve(moduleDir, filePath)];

    for (const candidate of candidates) {
        if (fs.existsSync(candidate) && fs.statSync(candidate).isFile()) {
            return candidate;
        }
    }

    return null;
}

/**
 * Resolves the Firebase Admin service account.
 *
 * @param {object} [options]
 * @param {object} [options.env=process.env]
 * @param {string} [options.cwd=process.cwd()]
 * @param {string} [options.moduleDir=__dirname]
 * @returns {{ serviceAccount: object, source: string, path: string|null }}
 * @throws {Error} when no source is configured, or a configured source is unusable
 */
function resolveFirebaseServiceAccount(options = {}) {
    const env = options.env || process.env;
    const cwd = options.cwd || process.cwd();
    const moduleDir = options.moduleDir || __dirname;

    for (const { name, decode } of INLINE_SOURCES) {
        const raw = env[name];
        if (!raw || !String(raw).trim()) continue;

        return {
            serviceAccount: parseServiceAccount(decode(String(raw).trim()), name),
            source: name,
            path: null
        };
    }

    const fileCandidates = [
        ...FILE_SOURCES.map(name => ({ name, value: env[name] })),
        { name: FALLBACK_FILE, value: FALLBACK_FILE }
    ];

    for (const { name, value } of fileCandidates) {
        if (!value || !String(value).trim()) continue;

        const resolved = resolveCandidatePath(String(value).trim(), { cwd, moduleDir });
        // A missing FALLBACK_FILE is the normal case on a real deployment, and an
        // env var pointing at nothing is worth reporting — but only once every
        // source has been tried, so the final message can list them all.
        if (!resolved) continue;

        // Read + parse rather than require(): a Render Secret File is mounted under
        // whatever name it was given, frequently with no .json extension, and
        // require() refuses those outright.
        return {
            serviceAccount: parseServiceAccount(fs.readFileSync(resolved, "utf8"), `${name} (${resolved})`),
            source: name,
            path: resolved
        };
    }

    throw new Error(
        "Missing Firebase service account. Set one of: " +
        [...INLINE_SOURCES.map(source => source.name), ...FILE_SOURCES].join(", ") +
        `, or place ${FALLBACK_FILE} next to server.js.`
    );
}

module.exports = {
    REQUIRED_FIELDS,
    FALLBACK_FILE,
    parseServiceAccount,
    resolveFirebaseServiceAccount
};

const express = require("express");
const admin = require("firebase-admin");
const jwt = require("jsonwebtoken");
const crypto = require("crypto");
const rateLimit = require("express-rate-limit");
const NodeCache = require("node-cache");
const helmet = require("helmet");
const cors = require("cors");
const fs = require("fs");
const { GoogleAuth } = require("google-auth-library");
const licenseLifecycle = require("./license-expiry");
const { parseServiceAccount, resolveFirebaseServiceAccount } = require("./firebase-credentials");
require("dotenv").config();

const FIREBASE_TIMEOUT_MS = Number(process.env.FIREBASE_OPERATION_TIMEOUT_MS || 8000);
const GOOGLE_SHEETS_GRANT_TIMEOUT_MS = Number(process.env.GOOGLE_SHEETS_GRANT_TIMEOUT_MS || 8000);
const GOOGLE_SHEETS_SCOPE = "https://www.googleapis.com/auth/spreadsheets";

// ==========================================
// ERROR HANDLER
// ==========================================
process.on("uncaughtException", err => console.error("[FATAL]", err));
process.on("unhandledRejection", err => console.error("[FATAL]", err));

// ==========================================
// ENV CONFIG
// ==========================================
if (!process.env.JWT_PRIVATE_KEY || !process.env.JWT_PUBLIC_KEY) {
    console.error("Missing JWT keys in Environment Variables");
    process.exit(1);
}

const formatKey = (k) => {
    if (!k) return "";
    return k.replace(/^"|"$/g, "").replace(/\\n/g, "\n");
};

const CONFIG = {
    PRIVATE: formatKey(process.env.JWT_PRIVATE_KEY),
    PUBLIC: formatKey(process.env.JWT_PUBLIC_KEY),

    ISSUER: "autojms-license-server",
    AUDIENCE: "autojms-desktop-client",

    DATAHUB_API_BASE_URL:
        String(process.env.DATAHUB_API_BASE_URL || "https://datahub.example.com").replace(/\/+$/g, ""),

    FIREBASE_DATABASE_URL:
        process.env.FIREBASE_DATABASE_URL ||
        "https://keyauthjms-default-rtdb.asia-southeast1.firebasedatabase.app/",

    DEFAULT_CHANNEL:
        process.env.DEFAULT_UPDATE_CHANNEL || "stable",

    // Lifecycle defaults. A license record may override graceDays per key;
    // offlineGraceHours is advisory and only forwarded to the client.
    DEFAULT_GRACE_DAYS:
        Number(process.env.LICENSE_GRACE_DAYS || licenseLifecycle.DEFAULT_GRACE_DAYS),

    OFFLINE_GRACE_HOURS:
        Number(process.env.LICENSE_OFFLINE_GRACE_HOURS || licenseLifecycle.DEFAULT_OFFLINE_GRACE_HOURS),

    BILLING_ANCHOR_DAY:
        Number(process.env.LICENSE_BILLING_ANCHOR_DAY || licenseLifecycle.BILLING_ANCHOR_DAY),

    // Site code is the DataHub tenant key. Every key in the fleet still ships
    // the "0000" placeholder, so enforcement stays opt-in until they are
    // migrated — flip REQUIRE_UNIQUE_SITE_CODE=1 once that is done.
    REQUIRE_UNIQUE_SITE_CODE:
        String(process.env.REQUIRE_UNIQUE_SITE_CODE || "").trim() === "1"
};

/** middleCode values that are not a real tenant identity. */
const PLACEHOLDER_SITE_CODES = new Set(["", "0000", "00000", "0", "DEFAULT", "NONE", "TBD"]);

const DATAHUB_MANIFESTS = {
    appManifest:
        "manifest/app-manifest.json",

    versionLatest:
        "manifest/version-latest.json",

    hashManifest:
        "manifest/hash-manifest.json",

    selectorUpdateManifest:
        "selector-updates/selector-update-manifest.json",

    smallUpdateManifest:
        "selector-updates/selector-update-manifest.json",

    tierDefinitions:
        "manifest/tier-definitions.json",

    publicConfig:
        "configs/public-config.json",

    runtimePolicy:
        "configs/runtime-policy.json",

    featurePolicy:
        "manifest/feature-policy.json",

    googleSheetsPolicy:
        `manifest/google-sheets-policy.json`,

    printPolicy:
        `manifest/print-policy.json`,

    fullStackPolicy:
        `manifest/fullstack-policy.json`,

    debugCapturePolicy:
        `manifest/debug-capture-policy.json`
};

// ==========================================
// DATAHUB LICENSE ASSERTION (RS256)
// ==========================================
// The DataHub API refuses to enroll a device without a signed license assertion. Render is the
// only component that knows whether a license is real, so it is the issuer; the DataHub host
// only ever holds the matching PUBLIC key (it rejects a private one on purpose).
//
// Wire format expected by RsaLicenseAssertionValidator:
//   v1rs256.<base64url(payload JSON)>.<base64url(RSASSA-PKCS1-v1_5 SHA-256 over the encoded payload)>
//
// Generate the key pair (never commit either half):
//   openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out datahub-license.key
//   openssl rsa -in datahub-license.key -pubout -out datahub-license.pub
// Render env → DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY (this server)
// DataHub env → DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY  (the VPS)
const boundedNumber = (value, fallback, min, max) => {
    const parsed = Number(value);
    if (!Number.isFinite(parsed)) return fallback;
    return Math.min(Math.max(Math.trunc(parsed), min), max);
};

const DATAHUB_ASSERTION = {
    PRIVATE_KEY: formatKey(process.env.DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY),
    ISSUER: process.env.DATAHUB_LICENSE_ASSERTION_ISSUER || "autojms-license",
    AUDIENCE: process.env.DATAHUB_LICENSE_ASSERTION_AUDIENCE || "autojms-datahub-enroll",
    CHANNEL: process.env.DATAHUB_CHANNEL || "production",
    TTL_SECONDS: boundedNumber(process.env.DATAHUB_LICENSE_ASSERTION_TTL_SECONDS, 300, 60, 3600),
    DEFAULT_SEATS: boundedNumber(process.env.DATAHUB_DEFAULT_SEATS, 3, 1, 500)
};

if (!DATAHUB_ASSERTION.PRIVATE_KEY) {
    console.warn("[datahub] DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY is not set — device enrollment stays closed.");
}

/**
 * Site codes a license may enroll against, uppercased the same way the DataHub validator
 * normalizes them. Falls back to middleCode so existing single-site licenses keep working
 * without a Firebase migration.
 */
function resolveLicenseSiteCodes(data, middleCode) {
    const raw = Array.isArray(data?.siteCodes)
        ? data.siteCodes
        : [data?.siteCode, data?.siteId, middleCode];

    return [...new Set(
        raw
            .map(code => String(code || "").trim().toUpperCase())
            .filter(Boolean)
    )];
}

/**
 * Mints a short-lived assertion. Returns null when the deployment has no signing key or the
 * license carries no site — both mean "cannot enroll", never "enroll unrestricted".
 */
function issueDataHubAssertion(data, middleCode) {
    if (!DATAHUB_ASSERTION.PRIVATE_KEY) return null;

    const siteCodes = resolveLicenseSiteCodes(data, middleCode);
    if (siteCodes.length === 0) return null;

    const expiresAt = Math.floor(Date.now() / 1000) + DATAHUB_ASSERTION.TTL_SECONDS;

    // PascalCase keys are required: the validator deserializes into LicenseAssertionPayload
    // with System.Text.Json defaults, which are case-sensitive. camelCase silently produces an
    // empty payload and a 401.
    const payload = {
        Channel: DATAHUB_ASSERTION.CHANNEL,
        SiteCodes: siteCodes,
        ExpiresAt: expiresAt,
        // The claim must be https or absent — the validator rejects any other scheme.
        DataHubUrl: CONFIG.DATAHUB_API_BASE_URL.startsWith("https://") ? CONFIG.DATAHUB_API_BASE_URL : null,
        Seats: boundedNumber(data?.seats, DATAHUB_ASSERTION.DEFAULT_SEATS, 1, 500),
        TokenVersion: boundedNumber(data?.tokenVersion, 1, 1, 1_000_000),
        Issuer: DATAHUB_ASSERTION.ISSUER,
        Audience: DATAHUB_ASSERTION.AUDIENCE
    };

    const encodedPayload = Buffer.from(JSON.stringify(payload), "utf8").toString("base64url");
    const signature = crypto
        .sign("sha256", Buffer.from(encodedPayload, "utf8"), {
            key: DATAHUB_ASSERTION.PRIVATE_KEY,
            padding: crypto.constants.RSA_PKCS1_PADDING
        })
        .toString("base64url");

    return {
        assertion: `v1rs256.${encodedPayload}.${signature}`,
        expiresAt,
        siteCodes
    };
}

// ==========================================
// FIREBASE INIT
// ==========================================
let firebaseCredential;
try {
    firebaseCredential = resolveFirebaseServiceAccount({ moduleDir: __dirname });
} catch (err) {
    // The only failure this process cannot recover from, so it stays an exit —
    // but the message now names which source was tried and why it was rejected,
    // instead of claiming nothing was configured.
    console.error("[firebase]", err.message);
    process.exit(1);
}

console.log(
    `[firebase] service account from ${firebaseCredential.source}, project_id: ${firebaseCredential.serviceAccount.project_id}`
);

admin.initializeApp({
    credential: admin.credential.cert(firebaseCredential.serviceAccount),
    databaseURL: CONFIG.FIREBASE_DATABASE_URL
});

// ==========================================
// APP INIT
// ==========================================
const app = express();

app.set("trust proxy", 1);
app.use(helmet());
// origin:false, not the default wildcard. Every caller here is a WinForms desktop
// using HttpClient, which does not perform a CORS check at all — the wildcard bought
// nothing and told any browser on the internet that it could read these responses
// cross-origin with whatever cookies it had. Preflight still gets a 204 so a
// misconfigured caller sees a CORS error rather than a hang.
app.use(cors({ origin: false }));
app.use(express.json({ limit: "512kb" }));

const limiter = rateLimit({
    windowMs: 60_000,
    max: 20
});

const heartbeatLimiter = rateLimit({
    windowMs: 60_000,
    max: 120
});

// The Firebase probes talk to the database, so they cost the same as a real
// request. Render's own health check only polls /health (no limiter needed for a
// static JSON reply), but /health/firebase was reachable by anyone at any rate.
const healthLimiter = rateLimit({
    windowMs: 60_000,
    max: 30
});

const googleSheetsGrantLimiter = rateLimit({
    windowMs: 60_000,
    max: 60
});

// Assertions are short-lived, so a station re-enrolls a handful of times a day. The cap is
// generous enough for a NAT'd office sharing one egress IP, tight enough to stop a signing loop.
const datahubAssertionLimiter = rateLimit({
    windowMs: 60_000,
    max: 60
});

const jtiCache = new NodeCache({ stdTTL: 3600 });
let googleSheetsAuthClient = null;
let googleSheetsServiceAccount = null;

// ==========================================
// HELPERS
// ==========================================
function withTimeout(promise, ms, label) {
    let timer;
    return Promise.race([
        promise,
        new Promise((_, reject) => {
            timer = setTimeout(() => reject(new Error(`${label}_TIMEOUT`)), ms);
        })
    ]).finally(() => clearTimeout(timer));
}

function isTimeoutError(error) {
    return typeof error?.message === "string" && error.message.endsWith("_TIMEOUT");
}

function maskLicenseKey(key) {
    const s = String(key || "");
    if (s.length <= 8) return "****";
    return `${s.slice(0, 4)}-****-${s.slice(-4)}`;
}

function sendTimeoutResponse(res) {
    return res.status(503).json({
        success: false,
        error: "FIREBASE_TIMEOUT",
        message: "License server timeout while verifying license."
    });
}

// The only tiers the desktop client knows how to enforce. A typo in Firebase
// ("Ultra ", "ULTRAA", "PRO") used to pass through untouched and land the
// station on the BASE entitlement set — a silent downgrade nobody notices
// until a customer reports a missing feature. Fail loudly instead.
const KNOWN_TIERS = new Set(["BASE", "ULTRA"]);

function normalizeTier(tier) {
    return String(tier || "BASE")
        .trim()
        .toUpperCase();
}

function isKnownTier(tier) {
    return KNOWN_TIERS.has(normalizeTier(tier));
}

function normalizeSiteCode(middleCode) {
    return String(middleCode || "").trim().toUpperCase();
}

// `Licenses/${licenseKey}` puts the caller's string straight into a Realtime
// Database path. Firebase forbids . $ # [ ] / and control characters in a key
// name: a slash walks to a different node, and any of the others throws inside
// ref(), which surfaces as a 500 that says nothing. This is a "cannot break the
// path" guard on purpose and not a format check — the fleet's key format is not
// recorded anywhere, and a stricter pattern would lock out live keys.
const LICENSE_KEY_PATTERN = /^[^.#$[\]/\u0000-\u001f\u007f]{4,128}$/;

/** HWID is stored as a value, never a path, so only length and control chars matter. */
const HWID_PATTERN = /^[^\u0000-\u001f\u007f]{8,256}$/;

/**
 * The lifecycle gate, in one place.
 *
 * verify-license was the only route that ran it, which meant expiry took effect
 * at the next app launch and nowhere else: while the app stayed open the
 * heartbeat kept minting fresh 60-minute tokens, the Sheets broker kept handing
 * out Google credentials, and DataHub re-enrollment kept being signed. A station
 * left running never expired at all.
 */
function evaluateLicenseRecord(data) {
    return licenseLifecycle.evaluateLicense(
        {
            status: data?.status,
            expiresAt: data?.expiresAt,
            graceDays: data?.graceDays ?? CONFIG.DEFAULT_GRACE_DAYS
        },
        Date.now()
    );
}

function isPlaceholderSiteCode(siteCode) {
    return PLACEHOLDER_SITE_CODES.has(normalizeSiteCode(siteCode));
}

function getClientIp(req) {
    return (
        req.headers["x-forwarded-for"]?.split(",")[0]?.trim() ||
        req.socket.remoteAddress ||
        ""
    );
}

function signAccessToken({ licenseKey, hwid, sessionId, tier }) {
    return jwt.sign(
        {
            key: licenseKey,
            hwid,
            sid: sessionId,
            tier,
            jti: crypto.randomUUID()
        },
        CONFIG.PRIVATE,
        {
            algorithm: "RS256",
            expiresIn: "60m",
            issuer: CONFIG.ISSUER,
            audience: CONFIG.AUDIENCE,
            keyid: "accessKey"
        }
    );
}

function createPublicError(statusCode, error, message) {
    const err = new Error(error);
    err.statusCode = statusCode;
    err.publicError = error;
    err.publicMessage = message;
    return err;
}

function loadGoogleSheetsServiceAccount() {
    if (googleSheetsServiceAccount) {
        return googleSheetsServiceAccount;
    }

    const filePath =
        process.env.GOOGLE_SHEETS_SERVICE_ACCOUNT_FILE ||
        process.env.FIREBASE_SERVICE_ACCOUNT_FILE;

    if (!filePath || !filePath.trim()) {
        throw new Error("Missing GOOGLE_SHEETS_SERVICE_ACCOUNT_FILE or FIREBASE_SERVICE_ACCOUNT_FILE in Environment Variables");
    }

    if (!fs.existsSync(filePath)) {
        throw new Error("GOOGLE_SHEETS_SERVICE_ACCOUNT_FILE not found");
    }

    // Same parser as the Firebase credential, so a malformed file produces one
    // wording rather than two, and "missing required fields" says which fields.
    const serviceAccount = parseServiceAccount(
        fs.readFileSync(filePath, "utf8"),
        "Google Sheets service account file"
    );

    googleSheetsServiceAccount = serviceAccount;
    console.log("[google-sheets] service account project_id:", serviceAccount.project_id);

    return googleSheetsServiceAccount;
}

async function getGoogleSheetsAccessGrant() {
    if (!googleSheetsAuthClient) {
        const googleSheetsCredential = loadGoogleSheetsServiceAccount();
        const auth = new GoogleAuth({
            credentials: googleSheetsCredential,
            scopes: [GOOGLE_SHEETS_SCOPE]
        });

        googleSheetsAuthClient = await withTimeout(
            auth.getClient(),
            GOOGLE_SHEETS_GRANT_TIMEOUT_MS,
            "GOOGLE_SHEETS_AUTH_CLIENT"
        );
    }

    const tokenResponse = await withTimeout(
        googleSheetsAuthClient.getAccessToken(),
        GOOGLE_SHEETS_GRANT_TIMEOUT_MS,
        "GOOGLE_SHEETS_ACCESS_TOKEN"
    );

    const accessToken = typeof tokenResponse === "string"
        ? tokenResponse
        : tokenResponse?.token;

    if (!accessToken) {
        throw new Error("GOOGLE_SHEETS_ACCESS_TOKEN_EMPTY");
    }

    const expiryMs = Number(googleSheetsAuthClient.credentials?.expiry_date || 0);
    const expiresAtMs = expiryMs > Date.now()
        ? expiryMs
        : Date.now() + 3600_000;

    return {
        accessToken,
        expiresAt: new Date(expiresAtMs).toISOString(),
        expiresInSeconds: Math.max(60, Math.floor((expiresAtMs - Date.now()) / 1000))
    };
}

async function verifyLicenseTokenAndSession(req) {
    const auth = req.headers.authorization || "";

    if (!auth.startsWith("Bearer ")) {
        throw createPublicError(401, "UNAUTHORIZED", "Missing license token.");
    }

    const token = auth.slice("Bearer ".length).trim();

    if (!token) {
        throw createPublicError(401, "UNAUTHORIZED", "Missing license token.");
    }

    let decoded;

    try {
        decoded = jwt.verify(token, CONFIG.PUBLIC, {
            algorithms: ["RS256"],
            issuer: CONFIG.ISSUER,
            audience: CONFIG.AUDIENCE
        });
    } catch {
        throw createPublicError(401, "UNAUTHORIZED", "Invalid or expired license token.");
    }

    const sessionRef = admin.database().ref(`sessions/${decoded.sid}`);
    const snap = await withTimeout(
        sessionRef.once("value"),
        FIREBASE_TIMEOUT_MS,
        "FIREBASE_GOOGLE_SHEETS_SESSION_READ"
    );

    if (!snap.exists()) {
        throw createPublicError(401, "SESSION_NOT_FOUND", "License session was revoked.");
    }

    const sessionData = snap.val();

    if (
        sessionData.status !== "active" ||
        sessionData.licenseKey !== decoded.key ||
        sessionData.hwid !== decoded.hwid
    ) {
        throw createPublicError(401, "SESSION_INACTIVE", "License session is not active.");
    }

    return { decoded, sessionData };
}

// ==========================================
// HEALTH CHECK
// ==========================================
app.get("/health", (req, res) => {
    res.json({
        ok: true,
        service: "autojms-license-server",
        time: Date.now()
    });
});

app.get("/health/firebase", healthLimiter, async (req, res) => {
    const started = Date.now();

    try {
        await withTimeout(
            admin.database().ref(".info/connected").once("value"),
            FIREBASE_TIMEOUT_MS,
            "FIREBASE_HEALTH_READ"
        );

        return res.json({
            ok: true,
            service: "firebase",
            elapsedMs: Date.now() - started
        });
    } catch (err) {
        // The raw message used to be echoed to the caller. A Firebase error string
        // carries the database URL, the project id, and sometimes the service
        // account email — none of which an anonymous caller needs, and all of which
        // an operator can read in the Render log instead.
        console.error("[health/firebase] probe failed", { error: err.message });

        return res.status(503).json({
            ok: false,
            error: isTimeoutError(err) ? "FIREBASE_TIMEOUT" : "FIREBASE_UNAVAILABLE",
            elapsedMs: Date.now() - started
        });
    }
});

// The /health/firebase probe only proves a socket to the database. This one proves
// the service account may actually READ /Licenses, which is the failure this server
// cannot survive and the one a rules change causes. limitToFirst(1) so a poll costs
// one small node, not the whole fleet, and no license content is ever returned.
app.get("/health/firebase/licenses", healthLimiter, async (req, res) => {
    const started = Date.now();

    try {
        const snap = await withTimeout(
            admin.database().ref("Licenses").limitToFirst(1).once("value"),
            FIREBASE_TIMEOUT_MS,
            "FIREBASE_HEALTH_LICENSES_READ"
        );

        return res.json({
            ok: true,
            service: "firebase-licenses",
            readable: true,
            // Whether the node has any child at all. Deliberately not a count and
            // never a key: an empty /Licenses on a live deployment means the wrong
            // database URL, and that is worth distinguishing from "cannot read".
            hasAny: snap.exists(),
            elapsedMs: Date.now() - started
        });
    } catch (err) {
        console.error("[health/firebase/licenses] probe failed", { error: err.message });

        return res.status(503).json({
            ok: false,
            service: "firebase-licenses",
            error: isTimeoutError(err) ? "FIREBASE_TIMEOUT" : "FIREBASE_UNAVAILABLE",
            elapsedMs: Date.now() - started
        });
    }
});

// ==========================================
// API 1: VERIFY LICENSE
// ==========================================
app.post("/api/verify-license", limiter, async (req, res) => {
    const requestId = crypto.randomUUID();
    const started = Date.now();

    try {
        const { licenseKey, hwid, exeHash, appVersion } = req.body || {};
        const maskedLicenseKey = maskLicenseKey(licenseKey);

        console.log(`[verify-license] start requestId=${requestId} license=${maskedLicenseKey}`);

        if (!licenseKey || !hwid) {
            return res.status(400).json({
                success: false,
                error: "MISSING_REQUIRED_FIELDS",
                message: "License key and HWID are required."
            });
        }

        // Checked before the string reaches ref(). A LICENSE_NOT_FOUND for a
        // malformed key would be the friendlier answer, but it is also a lie: the
        // key was never looked up.
        if (typeof licenseKey !== "string" || !LICENSE_KEY_PATTERN.test(licenseKey)) {
            console.warn("[verify-license] malformed license key", { requestId });

            return res.status(400).json({
                success: false,
                error: "LICENSE_KEY_INVALID",
                message: "License key contains characters that are not allowed."
            });
        }

        if (typeof hwid !== "string" || !HWID_PATTERN.test(hwid)) {
            return res.status(400).json({
                success: false,
                error: "HWID_INVALID",
                message: "HWID is not a valid hardware identifier."
            });
        }

        const ref = admin.database().ref(`Licenses/${licenseKey}`);

        console.log("[verify-license] firebase license read start");
        const licenseReadStarted = Date.now();
        const snap = await withTimeout(
            ref.once("value"),
            FIREBASE_TIMEOUT_MS,
            "FIREBASE_LICENSE_READ"
        );
        console.log(`[verify-license] firebase license read done elapsedMs=${Date.now() - licenseReadStarted}`);

        const data = snap.val();

        if (!data) {
            console.log(`[verify-license] license not found requestId=${requestId} elapsedMs=${Date.now() - started}`);
            return res.status(404).json({
                success: false,
                error: "LICENSE_NOT_FOUND",
                message: "License key not found."
            });
        }

        if (data.status !== "active") {
            return res.status(401).json({
                success: false,
                error: "LICENSE_INACTIVE",
                message: "License key is inactive or locked."
            });
        }

        // ---- Lifecycle gate -------------------------------------------------
        // expiresAt is anchored to 00:00 +07:00 on the 16th of a month. Records
        // that predate the field are perpetual, so the fleet in the field keeps
        // working until an expiry is backfilled.
        const lifecycle = evaluateLicenseRecord(data);

        if (!lifecycle.allowed) {
            console.warn("[LICENSE_EXPIRED]", {
                licenseKey: maskedLicenseKey,
                expiresAt: lifecycle.expiresAt,
                graceUntil: lifecycle.graceUntil
            });

            return res.status(403).json({
                success: false,
                error: "LICENSE_EXPIRED",
                message: "License key has expired.",
                expiresAt: lifecycle.expiresAt,
                graceUntil: lifecycle.graceUntil
            });
        }

        if (lifecycle.effectiveStatus === "grace") {
            console.warn("[LICENSE_GRACE]", {
                licenseKey: maskedLicenseKey,
                expiresAt: lifecycle.expiresAt,
                graceUntil: lifecycle.graceUntil
            });
        }

        // ---- Tier allowlist -------------------------------------------------
        if (!isKnownTier(data.tier)) {
            console.error("[LICENSE_TIER_INVALID]", {
                licenseKey: maskedLicenseKey,
                rawTier: String(data.tier ?? "")
            });

            return res.status(403).json({
                success: false,
                error: "LICENSE_TIER_INVALID",
                message: "License tier is not recognised. Contact support."
            });
        }

        const tier = normalizeTier(data.tier);
        const skipHashCheck = data.skipHashCheck === true;
        const middleCode = data.middleCode || "";

        // ---- Site code ------------------------------------------------------
        // middleCode IS the DataHub site code (owner decision, 2026-08-24), so a
        // shared placeholder means several customers land in one tenant.
        if (isPlaceholderSiteCode(middleCode)) {
            if (CONFIG.REQUIRE_UNIQUE_SITE_CODE) {
                console.error("[LICENSE_SITE_CODE_INVALID]", {
                    licenseKey: maskedLicenseKey,
                    middleCode
                });

                return res.status(403).json({
                    success: false,
                    error: "LICENSE_SITE_CODE_INVALID",
                    message: "License has no unique site code. Contact support."
                });
            }

            console.warn("[LICENSE_SITE_CODE_PLACEHOLDER]", {
                licenseKey: maskedLicenseKey,
                middleCode
            });
        }
        const modulePolicy = data.modulePolicy || {
            autoUpdate: true,
            silentUpdate: true,
            applyOnNextStartup: true
        };

        // Hash verification for protected builds.
        // Major update hash should be controlled by server env or future hash-manifest.
        if (!skipHashCheck) {
            const validHashesStr = process.env.VALID_EXE_HASHES || "";

            if (validHashesStr.trim() !== "") {
                const validHashes = validHashesStr
                    .split(",")
                    .map(h => h.trim().toLowerCase())
                    .filter(Boolean);

                const localHash = String(exeHash || "").toLowerCase();

                if (!localHash || !validHashes.includes(localHash)) {
                    console.warn("[HASH_INVALID]", {
                        licenseKey: maskedLicenseKey,
                        hasExeHash: Boolean(exeHash),
                        appVersion
                    });

                    return res.status(403).json({
                        success: false,
                        error: "HASH_INVALID",
                        message: "Application hash is invalid or outdated."
                    });
                }
            }
        }

        // HWID lock
        if (data.hwid && data.hwid !== hwid) {
            return res.status(401).json({
                success: false,
                error: "HWID_MISMATCH",
                message: "License key is already bound to another machine."
            });
        }

        if (!data.hwid) {
            const licenseUpdateStarted = Date.now();
            await withTimeout(
                ref.update({
                    hwid,
                    // ISO + explicit offset so a human reading the record in the
                    // Firebase console sees a date, not an epoch number.
                    activatedAt: licenseLifecycle.toVnIso(Date.now())
                }),
                FIREBASE_TIMEOUT_MS,
                "FIREBASE_LICENSE_UPDATE"
            );
            console.log(`[verify-license] firebase license update done elapsedMs=${Date.now() - licenseUpdateStarted}`);
        }

        // Clear old sessions of same license + same device
        const sessionsRef = admin.database().ref("sessions");
        const sessionsReadStarted = Date.now();
        const sessionsSnap = await withTimeout(
            sessionsRef
                .orderByChild("licenseKey")
                .equalTo(licenseKey)
                .once("value"),
            FIREBASE_TIMEOUT_MS,
            "FIREBASE_SESSIONS_READ"
        );
        console.log(`[verify-license] firebase sessions read done elapsedMs=${Date.now() - sessionsReadStarted}`);

        const updates = {};

        sessionsSnap.forEach(child => {
            const session = child.val();

            if (session.hwid === hwid) {
                updates[child.key] = null;
            }
        });

        if (Object.keys(updates).length > 0) {
            const sessionsUpdateStarted = Date.now();
            await withTimeout(
                sessionsRef.update(updates),
                FIREBASE_TIMEOUT_MS,
                "FIREBASE_SESSIONS_UPDATE"
            );
            console.log(`[verify-license] firebase sessions update done elapsedMs=${Date.now() - sessionsUpdateStarted}`);
        }

        // Create new session
        const sessionId = crypto.randomUUID();

        console.log("[verify-license] session write start");
        const sessionWriteStarted = Date.now();
        await withTimeout(
            admin.database().ref(`sessions/${sessionId}`).set({
                licenseKey,
                hwid,
                tier,
                middleCode,
                status: "active",
                appVersion: appVersion || "",
                ip: getClientIp(req),
                createdAt: Date.now(),
                lastPing: Date.now()
            }),
            FIREBASE_TIMEOUT_MS,
            "FIREBASE_SESSION_WRITE"
        );
        console.log(`[verify-license] session write done elapsedMs=${Date.now() - sessionWriteStarted}`);

        const token = signAccessToken({
            licenseKey,
            hwid,
            sessionId,
            tier
        });

        // Enrollment credential for the DataHub API. Minted here because Render is the only
        // component that has just proven the license is active and bound to this machine.
        const datahubAssertion = issueDataHubAssertion(data, middleCode);
        if (!datahubAssertion) {
            console.warn(`[verify-license] no DataHub assertion issued requestId=${requestId} license=${maskedLicenseKey}`);
        }

        // Backward compatibility only: legacy clients still read modulePolicy
        // from Render. New clients use DataHub runtime-policy as the feature
        // authority after Render authenticates license identity/session.
        console.log(`[verify-license] success elapsedMs=${Date.now() - started} requestId=${requestId}`);

        return res.json({
            payload: token,
            sid: sessionId,
            tier,
            middleCode,
            skipHashCheck,
            modulePolicy,

            license: {
                status: data.status || "active",
                tier,
                middleCode,
                // middleCode IS the site code; siteCode mirrors it in the shape
                // the DataHub API expects.
                siteCode: normalizeSiteCode(middleCode),
                skipHashCheck,
                modulePolicy,

                // Lifecycle. expiresAt is null on v1 records that have no
                // expiry yet; the client must treat null as "no expiry known"
                // and not as "expired".
                effectiveStatus: lifecycle.effectiveStatus,
                expiresAt: lifecycle.expiresAt,
                graceUntil: lifecycle.graceUntil,
                daysRemaining: lifecycle.daysRemaining,
                graceDays: Number.isFinite(Number(data.graceDays))
                    ? Number(data.graceDays)
                    : CONFIG.DEFAULT_GRACE_DAYS,
                offlineGraceHours: CONFIG.OFFLINE_GRACE_HOURS,
                billingAnchorDay: CONFIG.BILLING_ANCHOR_DAY,
                // Same bounds as the DataHub assertion so the two never disagree.
                seats: boundedNumber(data.seats, DATAHUB_ASSERTION.DEFAULT_SEATS, 1, 500)
            },

            cfg: {
                dataSpreadsheetId: data.dataSpreadsheetId || "",
                updateChannel: data.updateChannel || CONFIG.DEFAULT_CHANNEL
            },

            datahub: {
                apiBaseUrl: CONFIG.DATAHUB_API_BASE_URL,
                siteId: data.siteId || middleCode || "",
                // siteCode is what /api/v1/devices/enroll matches on; siteId above is kept for
                // older clients and is replaced by the GUID the enrollment response returns.
                siteCode: (datahubAssertion?.siteCodes?.[0]) || String(middleCode || "").toUpperCase(),
                licenseAssertion: datahubAssertion?.assertion || "",
                assertionExpiresAt: datahubAssertion?.expiresAt || 0,
                manifests: DATAHUB_MANIFESTS
            }
        });
    } catch (e) {
        console.error(`[verify-license] error requestId=${requestId} elapsedMs=${Date.now() - started}`, {
            error: e.message
        });

        if (isTimeoutError(e)) {
            return sendTimeoutResponse(res);
        }

        return res.status(500).json({
            success: false,
            error: "INTERNAL_ERROR",
            message: "License server internal error."
        });
    }
});

// ==========================================
// API 2: GOOGLE SHEETS TOKEN BROKER
// ==========================================
app.post("/api/google-sheets/grant", googleSheetsGrantLimiter, async (req, res) => {
    const requestId = crypto.randomUUID();
    const started = Date.now();

    try {
        console.log(`[google-sheets-grant] start requestId=${requestId}`);

        const { decoded } = await verifyLicenseTokenAndSession(req);
        const maskedLicenseKey = maskLicenseKey(decoded.key);

        const licenseSnap = await withTimeout(
            admin.database().ref(`Licenses/${decoded.key}`).once("value"),
            FIREBASE_TIMEOUT_MS,
            "FIREBASE_GOOGLE_SHEETS_LICENSE_READ"
        );

        if (!licenseSnap.exists()) {
            return res.status(404).json({
                ok: false,
                error: "LICENSE_NOT_FOUND",
                message: "License key not found."
            });
        }

        const licenseData = licenseSnap.val() || {};

        if (licenseData.status !== "active") {
            return res.status(403).json({
                ok: false,
                error: "LICENSE_INACTIVE",
                message: "License key is inactive or locked."
            });
        }

        // Same gate as verify-license. Without it an expired license kept receiving
        // Google credentials for as long as its access token stayed refreshable —
        // and the heartbeat refreshes that token every minute, so "as long as the
        // app stays open" meant indefinitely.
        const grantLifecycle = evaluateLicenseRecord(licenseData);

        if (!grantLifecycle.allowed) {
            console.warn("[LICENSE_EXPIRED]", {
                route: "google-sheets-grant",
                licenseKey: maskedLicenseKey,
                expiresAt: grantLifecycle.expiresAt,
                graceUntil: grantLifecycle.graceUntil
            });

            return res.status(403).json({
                ok: false,
                error: "LICENSE_EXPIRED",
                message: "License key has expired.",
                expiresAt: grantLifecycle.expiresAt,
                graceUntil: grantLifecycle.graceUntil
            });
        }

        const grant = await getGoogleSheetsAccessGrant();

        console.log(
            `[google-sheets-grant] success requestId=${requestId} license=${maskedLicenseKey} elapsedMs=${Date.now() - started}`
        );

        return res.json({
            ok: true,
            provider: "google-sheets-token-broker",
            accessToken: grant.accessToken,
            expiresAt: grant.expiresAt,
            expiresInSeconds: grant.expiresInSeconds,
            spreadsheetId: licenseData.dataSpreadsheetId || "",
            scopes: [GOOGLE_SHEETS_SCOPE]
        });
    } catch (e) {
        console.error(`[google-sheets-grant] error requestId=${requestId} elapsedMs=${Date.now() - started}`, {
            error: e.message
        });

        if (isTimeoutError(e)) {
            return res.status(503).json({
                ok: false,
                error: "GOOGLE_SHEETS_TIMEOUT",
                message: "Google Sheets token broker timeout."
            });
        }

        if (e.statusCode) {
            return res.status(e.statusCode).json({
                ok: false,
                error: e.publicError || "GOOGLE_SHEETS_GRANT_FAILED",
                message: e.publicMessage || "Google Sheets grant failed."
            });
        }

        if (
            String(e.message || "").includes("GOOGLE_SHEETS_SERVICE_ACCOUNT_FILE") ||
            String(e.message || "").toLowerCase().includes("service account") ||
            String(e.message || "").includes("GOOGLE_SHEETS_ACCESS_TOKEN_EMPTY")
        ) {
            return res.status(503).json({
                ok: false,
                error: "GOOGLE_SHEETS_BROKER_UNAVAILABLE",
                message: "Google Sheets token broker is not configured."
            });
        }

        return res.status(500).json({
            ok: false,
            error: "GOOGLE_SHEETS_GRANT_FAILED",
            message: "Google Sheets grant failed."
        });
    }
});

// ==========================================
// API 3: HEARTBEAT
// ==========================================
app.post("/api/heartbeat", heartbeatLimiter, async (req, res) => {
    const requestId = crypto.randomUUID();
    const started = Date.now();

    try {
        const auth = req.headers.authorization;

        if (!auth || !auth.startsWith("Bearer ")) {
            return res.status(401).json({
                action: "kill",
                reason: "Từ chối truy cập: Không tìm thấy Token."
            });
        }

        const token = auth.split(" ")[1];

        let decoded;

        try {
            decoded = jwt.verify(token, CONFIG.PUBLIC, {
                algorithms: ["RS256"],
                issuer: CONFIG.ISSUER,
                audience: CONFIG.AUDIENCE
            });
        } catch {
            return res.status(401).json({
                action: "kill",
                reason: "Token đã hết hạn hoặc không khả dụng."
            });
        }

        // A token with no jti is not a replay-safe token: jtiCache.has(undefined)
        // is always false and jtiCache.set(undefined, ...) records nothing, so the
        // whole replay check silently did nothing for such a token and it could be
        // reused forever. Every token this server issues carries one, so a missing
        // jti means the token was not issued here.
        if (!decoded.jti || typeof decoded.jti !== "string") {
            return res.status(401).json({
                action: "kill",
                reason: "Token thiếu định danh chống nhân bản."
            });
        }

        if (jtiCache.has(decoded.jti)) {
            return res.status(401).json({
                action: "kill",
                reason: "Phát hiện nhân bản gói tin mạng."
            });
        }

        jtiCache.set(decoded.jti, true);

        const sessionRef = admin.database().ref(`sessions/${decoded.sid}`);
        const snap = await withTimeout(
            sessionRef.once("value"),
            FIREBASE_TIMEOUT_MS,
            "FIREBASE_SESSION_READ"
        );

        if (!snap.exists()) {
            return res.status(401).json({
                action: "kill",
                reason: "Phiên làm việc đã bị Admin thu hồi."
            });
        }

        const sessionData = snap.val();

        if (sessionData.status !== "active") {
            return res.status(401).json({
                action: "kill",
                reason: "Phiên làm việc đã bị khóa."
            });
        }

        // ---- Lifecycle gate -------------------------------------------------
        // This is the only place expiry can take effect on a station that is
        // already running. verify-license runs at launch and nowhere else, so
        // without this the heartbeat kept minting a fresh 60-minute token every
        // minute for a license that had expired days earlier — a station left on
        // never expired at all, and revoking a license only worked if the customer
        // happened to restart the app.
        //
        // The extra Firebase read is the price: one small node per heartbeat, on
        // top of the session read that already happens here.
        const licenseSnap = await withTimeout(
            admin.database().ref(`Licenses/${decoded.key}`).once("value"),
            FIREBASE_TIMEOUT_MS,
            "FIREBASE_HEARTBEAT_LICENSE_READ"
        );
        const licenseData = licenseSnap.val();

        if (!licenseData) {
            // The license row is gone, so nothing can re-authorise this session.
            await withTimeout(sessionRef.remove(), FIREBASE_TIMEOUT_MS, "FIREBASE_SESSION_REMOVE");

            return res.status(401).json({
                action: "kill",
                error: "LICENSE_NOT_FOUND",
                reason: "Không tìm thấy license của phiên làm việc."
            });
        }

        const lifecycle = evaluateLicenseRecord(licenseData);

        if (!lifecycle.allowed) {
            console.warn("[LICENSE_EXPIRED]", {
                route: "heartbeat",
                licenseKey: maskLicenseKey(decoded.key),
                effectiveStatus: lifecycle.effectiveStatus,
                expiresAt: lifecycle.expiresAt,
                graceUntil: lifecycle.graceUntil
            });

            // Drop the session as well as refusing the token. Leaving it active
            // would let the next launch's verify-license see a live session for a
            // dead license, and it is the session that the Sheets broker and the
            // DataHub assertion route check.
            await withTimeout(sessionRef.remove(), FIREBASE_TIMEOUT_MS, "FIREBASE_SESSION_REMOVE");

            return res.status(401).json({
                action: "kill",
                error: lifecycle.effectiveStatus === "expired" ? "LICENSE_EXPIRED" : "LICENSE_INACTIVE",
                reason: lifecycle.effectiveStatus === "expired"
                    ? "License đã hết hạn. Vui lòng gia hạn để tiếp tục."
                    : "License đã bị khóa hoặc thu hồi.",
                expiresAt: lifecycle.expiresAt,
                graceUntil: lifecycle.graceUntil
            });
        }

        // A license re-bound to another machine must not keep this station alive.
        if (licenseData.hwid && decoded.hwid && licenseData.hwid !== decoded.hwid) {
            await withTimeout(sessionRef.remove(), FIREBASE_TIMEOUT_MS, "FIREBASE_SESSION_REMOVE");

            return res.status(401).json({
                action: "kill",
                error: "HWID_MISMATCH",
                reason: "License đã được gán cho một máy khác."
            });
        }

        await withTimeout(
            sessionRef.update({
                lastPing: Date.now()
            }),
            FIREBASE_TIMEOUT_MS,
            "FIREBASE_SESSION_UPDATE"
        );

        // The tier still comes from the token, not from the record just read: a tier
        // change takes effect on restart by owner decision (2026-08-24), and the
        // client caches its entitlement at launch. Reading it here would make the
        // heartbeat disagree with the running app instead of changing it.
        const tier = decoded.tier || sessionData.tier || "BASE";

        const newToken = signAccessToken({
            licenseKey: decoded.key,
            hwid: decoded.hwid,
            sessionId: decoded.sid,
            tier
        });

        return res.json({
            action: "continue",
            payload: newToken,
            tier,
            // Forwarded so a station can warn before expiry instead of dying at it.
            // Nothing on the client reads these yet — LicenseApiService is a
            // protected file — but they cost nothing and the data has to exist
            // before the warning can be built.
            effectiveStatus: lifecycle.effectiveStatus,
            expiresAt: lifecycle.expiresAt,
            graceUntil: lifecycle.graceUntil,
            daysRemaining: lifecycle.daysRemaining
        });
    } catch (e) {
        console.error(`[heartbeat] error requestId=${requestId} elapsedMs=${Date.now() - started}`, {
            error: e.message
        });

        if (isTimeoutError(e)) {
            return sendTimeoutResponse(res);
        }

        return res.status(500).json({
            success: false,
            error: "INTERNAL_ERROR",
            message: "License server internal error."
        });
    }
});

// ==========================================
// API 4: DATAHUB LICENSE ASSERTION (RE-ENROLL)
// ==========================================
// A device token issued by DataHub expires (24h by default) while the app may stay open for
// days. Rather than force a full re-activation, the client asks for a fresh assertion with the
// access token it already refreshes on every heartbeat, then re-enrolls.
app.post("/api/datahub/license-assertion", datahubAssertionLimiter, async (req, res) => {
    const requestId = crypto.randomUUID();
    const started = Date.now();

    try {
        const auth = req.headers.authorization;

        if (!auth || !auth.startsWith("Bearer ")) {
            return res.status(401).json({
                success: false,
                error: "UNAUTHORIZED",
                message: "Access token is required."
            });
        }

        let decoded;
        try {
            decoded = jwt.verify(auth.slice("Bearer ".length).trim(), CONFIG.PUBLIC, {
                algorithms: ["RS256"],
                issuer: CONFIG.ISSUER,
                audience: CONFIG.AUDIENCE
            });
        } catch {
            return res.status(401).json({
                success: false,
                error: "TOKEN_INVALID",
                message: "Access token expired or invalid."
            });
        }

        // Deliberately not consuming decoded.jti here: the heartbeat owns replay detection, and
        // burning the jti would kill the very session that is asking to stay connected.
        const sessionSnap = await withTimeout(
            admin.database().ref(`sessions/${decoded.sid}`).once("value"),
            FIREBASE_TIMEOUT_MS,
            "FIREBASE_SESSION_READ"
        );
        if (!sessionSnap.exists() || sessionSnap.val()?.status !== "active") {
            return res.status(401).json({
                success: false,
                error: "SESSION_REVOKED",
                message: "Session is no longer active."
            });
        }

        const licenseSnap = await withTimeout(
            admin.database().ref(`Licenses/${decoded.key}`).once("value"),
            FIREBASE_TIMEOUT_MS,
            "FIREBASE_LICENSE_READ"
        );
        const data = licenseSnap.val();
        if (!data || data.status !== "active") {
            return res.status(401).json({
                success: false,
                error: "LICENSE_INACTIVE",
                message: "License key is inactive or locked."
            });
        }
        // A license re-bound to another machine must not keep minting for this one.
        if (data.hwid && decoded.hwid && data.hwid !== decoded.hwid) {
            return res.status(401).json({
                success: false,
                error: "HWID_MISMATCH",
                message: "License key is bound to another machine."
            });
        }

        // Same gate as verify-license. An assertion is what buys a 24h DataHub
        // device token, so without this an expired license could keep renewing its
        // access to the whole data plane — writes included — one assertion at a time.
        const lifecycle = evaluateLicenseRecord(data);

        if (!lifecycle.allowed) {
            console.warn("[LICENSE_EXPIRED]", {
                route: "datahub-assertion",
                licenseKey: maskLicenseKey(decoded.key),
                expiresAt: lifecycle.expiresAt,
                graceUntil: lifecycle.graceUntil
            });

            return res.status(403).json({
                success: false,
                error: "LICENSE_EXPIRED",
                message: "License key has expired.",
                expiresAt: lifecycle.expiresAt,
                graceUntil: lifecycle.graceUntil
            });
        }

        const issued = issueDataHubAssertion(data, data.middleCode || "");
        if (!issued) {
            return res.status(503).json({
                success: false,
                error: "ASSERTION_UNAVAILABLE",
                message: "DataHub enrollment is not configured on this license server."
            });
        }

        console.log(`[datahub-assertion] issued requestId=${requestId} license=${maskLicenseKey(decoded.key)} elapsedMs=${Date.now() - started}`);
        return res.json({
            apiBaseUrl: CONFIG.DATAHUB_API_BASE_URL,
            siteCode: issued.siteCodes[0],
            licenseAssertion: issued.assertion,
            assertionExpiresAt: issued.expiresAt
        });
    } catch (e) {
        console.error(`[datahub-assertion] error requestId=${requestId} elapsedMs=${Date.now() - started}`, {
            error: e.message
        });

        if (isTimeoutError(e)) {
            return sendTimeoutResponse(res);
        }

        return res.status(500).json({
            success: false,
            error: "INTERNAL_ERROR",
            message: "License server internal error."
        });
    }
});

// ==========================================
// API 5: LOGOUT SESSION
// ==========================================
app.post("/api/logout", limiter, async (req, res) => {
    const requestId = crypto.randomUUID();
    const started = Date.now();

    try {
        const { sid } = req.body || {};

        if (!sid) {
            return res.json({ ok: true });
        }

        // This route used to be unauthenticated and unmetered: anyone who could
        // reach the host could burn Firebase writes, and anyone who learned a
        // sid could end that station's session. A caller must now prove it owns
        // the session it is ending.
        const auth = req.headers.authorization;

        if (!auth || !auth.startsWith("Bearer ")) {
            return res.status(401).json({
                success: false,
                error: "UNAUTHORIZED",
                message: "Missing access token."
            });
        }

        let decoded;

        try {
            decoded = jwt.verify(auth.slice("Bearer ".length).trim(), CONFIG.PUBLIC, {
                algorithms: ["RS256"],
                issuer: CONFIG.ISSUER,
                audience: CONFIG.AUDIENCE
            });
        } catch {
            return res.status(401).json({
                success: false,
                error: "UNAUTHORIZED",
                message: "Access token is expired or invalid."
            });
        }

        if (decoded.sid !== sid) {
            console.warn("[logout] token/sid mismatch", { requestId });

            return res.status(403).json({
                success: false,
                error: "SESSION_MISMATCH",
                message: "Token does not own this session."
            });
        }

        await withTimeout(
            admin.database().ref(`sessions/${sid}`).remove(),
            FIREBASE_TIMEOUT_MS,
            "FIREBASE_SESSION_REMOVE"
        );

        return res.json({ ok: true });
    } catch (e) {
        console.error(`[logout] error requestId=${requestId} elapsedMs=${Date.now() - started}`, {
            error: e.message
        });

        if (isTimeoutError(e)) {
            return sendTimeoutResponse(res);
        }

        return res.status(500).json({
            success: false,
            error: "INTERNAL_ERROR",
            message: "License server internal error."
        });
    }
});

// ==========================================
// START SERVER
// ==========================================
const PORT = process.env.PORT || 3000;

// listen() only when this file IS the entry point. Render runs `node server.js`,
// so production is unchanged; a test that requires this module gets the app
// without a bound socket, which is what makes the routes testable at all.
if (require.main === module) {
    app.listen(PORT, () => {
        console.log("AutoJMS Server Running port:", PORT);
    });
}

module.exports = app;

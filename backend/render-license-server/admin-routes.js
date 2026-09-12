"use strict";

// ==========================================================================
// Admin API — issue, list and maintain licence keys
// ==========================================================================
// Everything the owner used to do by hand in the Firebase console: read the
// fleet, mint a key, push an expiry forward, revoke, unbind a machine.
//
// It lives in its own router rather than in server.js for two reasons. The
// obvious one is size — server.js is already 2k lines. The load-bearing one is
// that this is the only surface on this process that WRITES to /Licenses.
// Everything in server.js reads a licence and decides whether a station may
// run; the routes below decide what a licence IS. Keeping that behind one
// mount point means the authentication for it is also in one place, and a
// route added here cannot accidentally inherit a desktop-client limiter.
//
// The module is self-contained on purpose: server.js requires it, so it cannot
// require server.js back. The few helpers duplicated from there (withTimeout,
// maskLicenseKey, the Firebase path guard) are small, pure and stable; each
// carries a pointer to its sibling.
// ==========================================================================

const crypto = require("crypto");
const express = require("express");
const admin = require("firebase-admin");
const rateLimit = require("express-rate-limit");

const {
    TZ_OFFSET_MINUTES,
    BILLING_ANCHOR_DAY,
    DEFAULT_GRACE_DAYS,
    computeExpiry,
    evaluateLicense,
    parseInstant,
    toVnIso
} = require("./license-expiry");

const router = express.Router();

// ==========================================
// CONFIG
// ==========================================

/**
 * The shared secret every admin request must present in X-Admin-Token.
 *
 * There is deliberately NO default value here, and this is a departure from the
 * task as written (which asked for a fallback password for dev/staging). This
 * repository is PUBLIC. A default baked into this file is not a password — it
 * is a published one, on the endpoints that mint ULTRA licences and revoke
 * paying customers, on a server that also signs DataHub enrolment assertions.
 *
 * Unset therefore means OFF, not "open": every route below answers 503 and
 * writes nothing. One environment variable on Render turns it on. Nothing is
 * logged that could reveal the value, which is why boot does not print a
 * generated fallback either — on Render stdout goes to a log viewer.
 */
const MIN_ADMIN_TOKEN_LENGTH = 16;

const ADMIN_TOKEN = (() => {
    const raw = String(process.env.ADMIN_SECRET_TOKEN || "").trim();
    if (!raw) return null;
    // A four-character admin token is the same hole as a default one, and it
    // would fail silently — the operator would believe the API was protected.
    if (raw.length < MIN_ADMIN_TOKEN_LENGTH) return null;
    return raw;
})();

const ADMIN_DISABLED_REASON = (() => {
    const raw = String(process.env.ADMIN_SECRET_TOKEN || "").trim();
    if (!raw) return "ADMIN_SECRET_TOKEN is not set.";
    if (raw.length < MIN_ADMIN_TOKEN_LENGTH) {
        return `ADMIN_SECRET_TOKEN is shorter than ${MIN_ADMIN_TOKEN_LENGTH} characters.`;
    }
    return null;
})();

/** Shared with server.js's Firebase calls so both halves have one knob. */
const FIREBASE_TIMEOUT_MS = Number(process.env.FIREBASE_OPERATION_TIMEOUT_MS || 8000);

/**
 * Mirrors CONFIG.DEFAULT_GRACE_DAYS / CONFIG.BILLING_ANCHOR_DAY in server.js.
 *
 * Not cosmetic: if the dashboard evaluated a record with a different grace
 * window than /api/verify-license does, the owner would read "grace" here while
 * the station in the field was already being refused — the one discrepancy that
 * makes a licence dashboard worse than no dashboard.
 */
const GRACE_DAYS = Number(process.env.LICENSE_GRACE_DAYS || DEFAULT_GRACE_DAYS);
const ANCHOR_DAY = Number(process.env.LICENSE_BILLING_ANCHOR_DAY || BILLING_ANCHOR_DAY);

/** The two tiers the desktop client knows how to enforce (server.js:409). */
const KNOWN_TIERS = new Set(["BASE", "ULTRA"]);

/** Ten years of monthly terms. A bound, so a fat-fingered 10000 cannot be sold. */
const MAX_TERMS = 120;

/** Free-text the owner types; bounded so a paste cannot bloat the record. */
const MAX_NOTES_LENGTH = 500;

/** A Google Sheet id is 44 characters; the ceiling is slack, not a format. */
const MAX_SPREADSHEET_ID_LENGTH = 200;

// ==========================================
// HELPERS
// ==========================================

// Same JSON-line shape as server.js's logEvent, so an operator filtering
// Render's log viewer on `event` sees admin actions beside licence checks.
// Contract is also the same: identifiers only, never secrets.
function logEvent(level, event, fields) {
    const sink = console[{ info: "log", warn: "warn", error: "error" }[level] || "log"];

    try {
        sink(JSON.stringify({ level, event, ...(fields || {}) }));
    } catch {
        sink(`{"level":"${level}","event":"${event}","logError":"UNSERIALIZABLE_FIELDS"}`);
    }
}

/** Mirrors server.js:391. Licence keys reach the log masked, never whole. */
function maskLicenseKey(key) {
    const s = String(key || "");
    if (s.length <= 8) return "****";
    return `${s.slice(0, 4)}-****-${s.slice(-4)}`;
}

/** Mirrors server.js:377. A Firebase call that never settles must not hold a request open. */
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

/**
 * Characters Firebase forbids in a Realtime Database key name.
 *
 * The authority is Firebase, not server.js — this is the same constraint
 * LICENSE_KEY_PATTERN encodes at server.js:431, arrived at independently
 * because a require() back into server.js would be a cycle. A slash walks to a
 * different node; the rest throw inside ref() and surface as a 500 saying
 * nothing. Deliberately a "cannot break the path" guard and not a format check:
 * keys already in the field predate the XXXX-CODE-XXXX shape.
 */
const LICENSE_KEY_PATTERN = /^[^.#$[\]/\u0000-\u001f\u007f]{4,128}$/;

/** The post-office code that forms the middle group of a new key. */
const MIDDLE_CODE_PATTERN = /^[A-Z0-9]{2,32}$/;

/** Uppercase alphanumerics only: every one of them is safe in a Firebase path. */
const KEY_ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
const KEY_GROUP_LENGTH = 4;

function randomKeyGroup() {
    let group = "";
    // randomInt, not Math.random() * length: unbiased, and these four characters
    // are the entire guess-resistance of a key that is otherwise predictable
    // (the middle group is a public post-office code).
    for (let i = 0; i < KEY_GROUP_LENGTH; i += 1) {
        group += KEY_ALPHABET[crypto.randomInt(KEY_ALPHABET.length)];
    }
    return group;
}

/** `XXXX-middlecode-XXXX`, e.g. JMXS-214A03-22B1. */
function buildLicenseKey(middleCode) {
    return `${randomKeyGroup()}-${middleCode}-${randomKeyGroup()}`;
}

/**
 * "DD-MM-YYYY HH:mm" as read in Asia/Ho_Chi_Minh.
 *
 * The shape every existing /Licenses record already carries, and the one
 * parseInstant() reads back as VN local time. A new record written in a
 * different shape would still parse, but the console would stop being
 * skimmable — half the fleet in one format and half in another.
 */
function toVnLegacyStamp(ms) {
    const shifted = new Date(ms + TZ_OFFSET_MINUTES * 60_000);
    const pad = n => String(n).padStart(2, "0");
    return (
        `${pad(shifted.getUTCDate())}-${pad(shifted.getUTCMonth() + 1)}-${shifted.getUTCFullYear()} ` +
        `${pad(shifted.getUTCHours())}:${pad(shifted.getUTCMinutes())}`
    );
}

function licenseRef(key) {
    return admin.database().ref(`Licenses/${key}`);
}

function fail(res, statusCode, error, message) {
    return res.status(statusCode).json({ success: false, error, message });
}

/**
 * Express 4 does not catch a rejected promise from an async handler: it becomes
 * an unhandled rejection and the request hangs until the client gives up. Every
 * route below is wrapped, so a Firebase outage answers 503 instead.
 */
function asyncRoute(handler) {
    return (req, res, next) => {
        Promise.resolve(handler(req, res, next)).catch(err => {
            if (res.headersSent) return;

            if (isTimeoutError(err)) {
                logEvent("error", "admin.firebase_timeout", { route: req.path });
                fail(res, 503, "FIREBASE_TIMEOUT", "Firebase không phản hồi kịp, thử lại sau.");
                return;
            }

            // err.message can carry the database URL and project id, so it is
            // logged and never returned — same rule as server.js's routes.
            logEvent("error", "admin.route_failed", { route: req.path, message: err?.message });
            fail(res, 500, "ADMIN_INTERNAL_ERROR", "Lỗi nội bộ khi xử lý yêu cầu quản trị.");
        });
    };
}

// ==========================================
// VALIDATION
// ==========================================

function normalizeTier(raw) {
    return String(raw || "").trim().toUpperCase();
}

/**
 * Parses the term selector shared by create and extend.
 *
 * Perpetual is spelled as an absent/empty/zero term and yields expiresAt null —
 * which is what evaluateLicense() reads as a v1 record that never expires.
 * Returns null when the value is present but unusable, so a typo is refused
 * rather than silently sold as a lifetime licence.
 */
function parseTerms(raw, { allowPerpetual }) {
    // Only a number or a string is a term selector. Without this, String([])
    // is "" and reads as perpetual, and Number(true) is 1 and reads as a
    // one-month sale — two ways for a malformed body to quietly become a
    // licence nobody meant to issue.
    if (raw !== null && raw !== undefined && typeof raw !== "number" && typeof raw !== "string") {
        return null;
    }

    const isPerpetual =
        raw === null ||
        raw === undefined ||
        raw === "" ||
        raw === 0 ||
        String(raw).trim().toLowerCase() === "0" ||
        String(raw).trim().toLowerCase() === "perpetual";

    if (isPerpetual) {
        return allowPerpetual ? { perpetual: true, terms: 0 } : null;
    }

    const parsed = Number(raw);
    if (!Number.isInteger(parsed) || parsed < 1 || parsed > MAX_TERMS) return null;
    return { perpetual: false, terms: parsed };
}

function sanitizeNotes(raw) {
    // Anything that is not text becomes empty rather than "[object Object]".
    if (typeof raw !== "string" && typeof raw !== "number") return "";
    // Control characters would survive into the console and the dashboard table.
    return String(raw).replace(/[\u0000-\u001f\u007f]/g, " ").trim().slice(0, MAX_NOTES_LENGTH);
}

/**
 * The Google Sheet id the owner pastes for an ULTRA key.
 *
 * Same treatment as notes and for the same reason: free text that lands in a
 * Firebase value and is read back by the desktop client, so it is scrubbed and
 * bounded rather than trusted. Deliberately NOT matched against Google's id
 * shape — the owner pastes whatever Google handed them, and a format guess
 * here would refuse a real sale to enforce a rule nobody wrote down.
 */
function sanitizeSpreadsheetId(raw) {
    if (typeof raw !== "string" && typeof raw !== "number") return "";
    // Everything outside printable ASCII goes: a Google id is [A-Za-z0-9_-], so
    // a control character or a Vietnamese letter arriving in this field is a
    // paste accident rather than data worth storing.
    return String(raw).replace(/[^ -~]/g, "").trim().slice(0, MAX_SPREADSHEET_ID_LENGTH);
}

/** Reads and validates :key, answering the client itself when it is unusable. */
async function loadLicense(req, res) {
    const key = String(req.params.key || "");

    if (!LICENSE_KEY_PATTERN.test(key)) {
        fail(res, 400, "INVALID_LICENSE_KEY", "Mã license không hợp lệ.");
        return null;
    }

    const snapshot = await withTimeout(licenseRef(key).once("value"), FIREBASE_TIMEOUT_MS, "FIREBASE");
    if (!snapshot.exists()) {
        fail(res, 404, "LICENSE_NOT_FOUND", "Không tìm thấy license này.");
        return null;
    }

    const record = snapshot.val();
    if (!record || typeof record !== "object") {
        fail(res, 409, "LICENSE_RECORD_MALFORMED", "Bản ghi license không phải object, cần sửa tay trên Firebase.");
        return null;
    }

    return { key, record };
}

/**
 * Decides the key the new record will be written at.
 *
 * The dashboard now shows a candidate key before the owner commits — the
 * "General Key" button — so the client may send back the exact string it
 * displayed. That is a convenience, not a trust boundary: the shape is
 * re-checked here, the middle group has to be the middle code that was just
 * validated, and the node has to be free. A bad one is refused rather than
 * quietly corrected, because a key the dashboard already showed is a key the
 * owner may already have pasted into a chat window.
 *
 * Answers the client itself on refusal and returns null — same contract as
 * loadLicense() above.
 */
async function resolveLicenseKey(body, middleCode, res) {
    // `customKey` is accepted as an alias so a caller that spells it the other
    // way gets its key honoured instead of silently receiving a random one.
    const requested = String(body?.key ?? body?.customKey ?? "").trim().toUpperCase();

    if (requested) {
        // Interpolating a request value into a RegExp is normally how a
        // catastrophic backtrack gets in. It is safe exactly here: middleCode
        // has already passed MIDDLE_CODE_PATTERN, so it is 2-32 characters of
        // [A-Z0-9] — no metacharacter, no quantifier, no alternation.
        const shape = new RegExp(`^[A-Z0-9]{${KEY_GROUP_LENGTH}}-${middleCode}-[A-Z0-9]{${KEY_GROUP_LENGTH}}$`);
        if (!shape.test(requested)) {
            fail(
                res,
                400,
                "INVALID_LICENSE_KEY",
                `Mã license phải đúng dạng XXXX-${middleCode}-XXXX (X là chữ in hoa hoặc số).`
            );
            return null;
        }

        const existing = await withTimeout(licenseRef(requested).once("value"), FIREBASE_TIMEOUT_MS, "FIREBASE");
        if (existing.exists()) {
            // The write below is a set(), so reusing a taken key would replace a
            // live customer's record outright. Refused, never re-rolled behind
            // the owner's back.
            fail(res, 409, "LICENSE_KEY_TAKEN", "Mã license này đã tồn tại, bấm General Key để sinh mã khác.");
            return null;
        }

        return requested;
    }

    // Four random characters per group is 36^8 ≈ 2.8e12 keys, so a collision is
    // not a realistic event — but for the same set() reason, one read is a cheap
    // price for making it impossible rather than improbable.
    for (let attempt = 0; attempt < 5; attempt += 1) {
        const candidate = buildLicenseKey(middleCode);
        const existing = await withTimeout(licenseRef(candidate).once("value"), FIREBASE_TIMEOUT_MS, "FIREBASE");
        if (!existing.exists()) return candidate;
    }

    logEvent("error", "admin.key_generation_exhausted", { middleCode });
    fail(res, 503, "KEY_GENERATION_FAILED", "Không sinh được mã license mới, thử lại.");
    return null;
}

// ==========================================
// AUTHENTICATION
// ==========================================

/**
 * Constant-time comparison of two secrets of unknown length.
 *
 * timingSafeEqual throws on a length mismatch, and comparing lengths first
 * leaks the length. Hashing both sides makes the inputs the same size no matter
 * what arrived, so the only thing an attacker can time is SHA-256 of their own
 * guess.
 */
function tokensMatch(provided, expected) {
    const a = crypto.createHash("sha256").update(String(provided), "utf8").digest();
    const b = crypto.createHash("sha256").update(String(expected), "utf8").digest();
    return crypto.timingSafeEqual(a, b);
}

function requireAdminToken(req, res, next) {
    if (!ADMIN_TOKEN) {
        // Answered the same way for every caller, authenticated or not: with no
        // token configured there is nothing to authenticate against.
        return fail(
            res,
            503,
            "ADMIN_API_DISABLED",
            "Admin API chưa được bật. Đặt biến môi trường ADMIN_SECRET_TOKEN (tối thiểu " +
                `${MIN_ADMIN_TOKEN_LENGTH} ký tự) rồi khởi động lại server.`
        );
    }

    const provided = req.get("x-admin-token");
    if (!provided || !tokensMatch(provided, ADMIN_TOKEN)) {
        logEvent("warn", "admin.auth_rejected", {
            route: req.path,
            reason: provided ? "TOKEN_MISMATCH" : "TOKEN_MISSING"
        });
        return fail(res, 401, "ADMIN_UNAUTHORIZED", "Sai Admin Token.");
    }

    return next();
}

// ==========================================
// RATE LIMITS
// ==========================================

// Generous, because this is one human clicking: a page load fans out to one
// list call, and the heaviest real burst is a refresh after a bulk of renewals.
// It exists to bound a runaway dashboard tab, not to police the owner.
const adminLimiter = rateLimit({
    windowMs: 60_000,
    max: 120
});

// The one that matters. X-Admin-Token is a bearer secret with no lockout of its
// own, so an open guessing loop is the realistic attack on this surface.
// skipSuccessfulRequests with a custom predicate counts ONLY 401s: an operator
// who sends ten malformed bodies is not locked out of their own dashboard, and
// a guesser gets ten attempts per quarter hour per IP.
const adminAuthFailureLimiter = rateLimit({
    windowMs: 15 * 60_000,
    max: 10,
    skipSuccessfulRequests: true,
    requestWasSuccessful: (req, res) => res.statusCode !== 401,
    message: {
        success: false,
        error: "ADMIN_AUTH_THROTTLED",
        message: "Quá nhiều lần nhập sai Admin Token. Thử lại sau 15 phút."
    }
});

// Order matters: the failure limiter has to be upstream of the check whose 401
// it counts, and both have to be upstream of every route.
router.use(adminLimiter);
router.use(adminAuthFailureLimiter);
router.use(requireAdminToken);

// ==========================================
// GET /api/admin/licenses
// ==========================================
/**
 * The whole fleet, each record put through the same lifecycle evaluation the
 * desktop client is subject to.
 *
 * `expiresAt` is the STORED value normalised, not evaluateLicense's — that one
 * is null for anything revoked, which would blank the expiry column for exactly
 * the keys an owner is most likely to be inspecting. effectiveStatus and
 * daysRemaining still come from the evaluation, so the lifecycle answer is the
 * server's and only the date is passed through.
 */
router.get(
    "/licenses",
    asyncRoute(async (req, res) => {
        const snapshot = await withTimeout(
            admin.database().ref("Licenses").once("value"),
            FIREBASE_TIMEOUT_MS,
            "FIREBASE"
        );

        const node = snapshot.val();
        const entries = node && typeof node === "object" && !Array.isArray(node) ? Object.entries(node) : [];
        const now = Date.now();

        const licenses = entries
            .filter(([, record]) => record && typeof record === "object")
            .map(([key, record]) => {
                const evaluation = evaluateLicense(
                    {
                        status: record.status,
                        expiresAt: record.expiresAt,
                        graceDays: record.graceDays ?? GRACE_DAYS
                    },
                    now
                );

                const storedExpiryMs = parseInstant(record.expiresAt);

                return {
                    key,
                    middleCode: String(record.middleCode || ""),
                    tier: normalizeTier(record.tier) || "BASE",
                    status: String(record.status || "unknown"),
                    effectiveStatus: evaluation.effectiveStatus,
                    expiresAt: storedExpiryMs === null ? null : toVnIso(storedExpiryMs),
                    daysRemaining: evaluation.daysRemaining,
                    hwid: String(record.hwid || ""),
                    notes: String(record.notes || "")
                };
            });

        // Soonest to expire first, so the keys needing attention are at the top
        // before the operator has touched a filter. Perpetual and revoked keys
        // (daysRemaining null) sort last, then alphabetically for a stable order.
        licenses.sort((a, b) => {
            if (a.daysRemaining === null && b.daysRemaining === null) return a.key.localeCompare(b.key);
            if (a.daysRemaining === null) return 1;
            if (b.daysRemaining === null) return -1;
            if (a.daysRemaining !== b.daysRemaining) return a.daysRemaining - b.daysRemaining;
            return a.key.localeCompare(b.key);
        });

        return res.json({ success: true, count: licenses.length, licenses });
    })
);

// ==========================================
// POST /api/admin/licenses/create
// ==========================================
router.post(
    "/licenses/create",
    asyncRoute(async (req, res) => {
        const middleCode = String(req.body?.middleCode || "").trim().toUpperCase();
        if (!MIDDLE_CODE_PATTERN.test(middleCode)) {
            return fail(
                res,
                400,
                "INVALID_MIDDLE_CODE",
                "Mã bưu cục chỉ gồm chữ và số (A-Z, 0-9), dài 2-32 ký tự."
            );
        }

        // A convenience pre-check, not the enforcement point: PLACEHOLDER_SITE_CODES
        // in server.js is what /api/verify-license actually refuses. Catching it
        // here means the owner learns at issue time instead of when the station
        // fails to enrol days later.
        if (/^0+$/.test(middleCode)) {
            return fail(
                res,
                400,
                "PLACEHOLDER_MIDDLE_CODE",
                "Mã bưu cục toàn số 0 bị verify-license từ chối; nhập mã bưu cục thật."
            );
        }

        const tier = normalizeTier(req.body?.tier);
        if (!KNOWN_TIERS.has(tier)) {
            return fail(res, 400, "INVALID_TIER", "Tier chỉ nhận BASE hoặc ULTRA.");
        }

        const term = parseTerms(req.body?.terms, { allowPerpetual: true });
        if (term === null) {
            return fail(res, 400, "INVALID_TERMS", `Kỳ hạn phải là số nguyên 1-${MAX_TERMS}, hoặc bỏ trống cho vĩnh viễn.`);
        }

        const notes = sanitizeNotes(req.body?.notes);
        // Only ULTRA reads a sheet. Storing one on a BASE record would make the
        // console show a key as provisioned while the client ignores the field.
        const dataSpreadsheetId = tier === "ULTRA" ? sanitizeSpreadsheetId(req.body?.dataSpreadsheetId) : "";
        // Defaults to the spec's true: the fleet's records carry it, and an
        // omitted field must not silently turn hash checking back on for a key
        // the owner did not mean to lock down.
        const skipHashCheck = req.body?.skipHashCheck === undefined ? true : Boolean(req.body.skipHashCheck);
        // Same default rule one level down, and the reason `!== false` rather
        // than Boolean(): a body with no modulePolicy at all must leave all
        // three switches ON, which is what every record already in the fleet
        // carries. Only an explicit false turns one off.
        const modulePolicy = {
            autoUpdate: req.body?.modulePolicy?.autoUpdate !== false,
            silentUpdate: req.body?.modulePolicy?.silentUpdate !== false,
            applyOnNextStartup: req.body?.modulePolicy?.applyOnNextStartup !== false
        };

        const createdAtMs = Date.now();
        const expiry = term.perpetual
            ? null
            : computeExpiry(createdAtMs, { terms: term.terms, anchorDay: ANCHOR_DAY });

        // Either the key the dashboard previewed, or a fresh random one. Both
        // paths confirm the node is free before the set() below.
        const licenseKey = await resolveLicenseKey(req.body, middleCode, res);
        if (!licenseKey) return undefined;

        const record = {
            createdAt: toVnLegacyStamp(createdAtMs),
            status: "active",
            tier,
            hwid: "",
            middleCode,
            skipHashCheck,
            modulePolicy,
            dataSpreadsheetId,
            // Realtime Database drops a null child rather than storing it, so a
            // perpetual key simply has no expiresAt — which is exactly the v1
            // record shape evaluateLicense() treats as never expiring.
            expiresAt: expiry ? expiry.expiresAt : null,
            notes
        };

        await withTimeout(licenseRef(licenseKey).set(record), FIREBASE_TIMEOUT_MS, "FIREBASE");

        logEvent("info", "admin.license_created", {
            key: maskLicenseKey(licenseKey),
            middleCode,
            tier,
            terms: term.perpetual ? "perpetual" : term.terms,
            expiresAt: record.expiresAt
        });

        return res.status(201).json({
            success: true,
            key: licenseKey,
            license: {
                key: licenseKey,
                middleCode,
                tier,
                status: "active",
                effectiveStatus: "active",
                expiresAt: record.expiresAt,
                daysRemaining: expiry ? Math.ceil((expiry.expiresAtMs - createdAtMs) / 86_400_000) : null,
                hwid: "",
                notes
            }
        });
    })
);

// ==========================================
// POST /api/admin/licenses/:key/extend
// ==========================================
/**
 * Pushes the expiry forward by whole anchor-to-anchor months.
 *
 * The new term starts at the CURRENT expiry, not at now, so a renewal bought
 * early does not forfeit the days already paid for — computeExpiry then snaps
 * the result back onto the 16th. The one exception is a key that lapsed long
 * ago: continuing from a year-old expiry would produce a renewal that is still
 * in the past, so that case restarts from today.
 */
router.post(
    "/licenses/:key/extend",
    asyncRoute(async (req, res) => {
        const loaded = await loadLicense(req, res);
        if (!loaded) return undefined;

        const term = parseTerms(req.body?.terms, { allowPerpetual: false });
        if (term === null) {
            return fail(res, 400, "INVALID_TERMS", `Số kỳ hạn phải là số nguyên từ 1 đến ${MAX_TERMS}.`);
        }

        const currentExpiryMs = parseInstant(loaded.record.expiresAt);
        if (currentExpiryMs === null) {
            // Not an error the owner can act on by retrying, so say what it is:
            // a key with no expiry already runs forever.
            return fail(
                res,
                409,
                "LICENSE_IS_PERPETUAL",
                "License này vĩnh viễn (không có hạn), không cần gia hạn."
            );
        }

        const now = Date.now();
        let next = computeExpiry(currentExpiryMs, { terms: term.terms, anchorDay: ANCHOR_DAY });
        if (next.expiresAtMs <= now) {
            next = computeExpiry(now, { terms: term.terms, anchorDay: ANCHOR_DAY });
        }

        await withTimeout(
            licenseRef(loaded.key).update({ expiresAt: next.expiresAt }),
            FIREBASE_TIMEOUT_MS,
            "FIREBASE"
        );

        logEvent("info", "admin.license_extended", {
            key: maskLicenseKey(loaded.key),
            terms: term.terms,
            from: toVnIso(currentExpiryMs),
            to: next.expiresAt
        });

        return res.json({
            success: true,
            key: loaded.key,
            previousExpiresAt: toVnIso(currentExpiryMs),
            expiresAt: next.expiresAt,
            daysRemaining: Math.ceil((next.expiresAtMs - now) / 86_400_000)
        });
    })
);

// ==========================================
// POST /api/admin/licenses/:key/toggle-status
// ==========================================
router.post(
    "/licenses/:key/toggle-status",
    asyncRoute(async (req, res) => {
        const loaded = await loadLicense(req, res);
        if (!loaded) return undefined;

        const current = String(loaded.record.status || "").trim().toLowerCase();
        // Anything that is not exactly "active" is already being refused by
        // verify-license, so the only useful move from there is to open it.
        const next = current === "active" ? "revoked" : "active";

        await withTimeout(licenseRef(loaded.key).update({ status: next }), FIREBASE_TIMEOUT_MS, "FIREBASE");

        logEvent("info", "admin.license_status_toggled", {
            key: maskLicenseKey(loaded.key),
            from: current || "unknown",
            to: next
        });

        return res.json({ success: true, key: loaded.key, status: next });
    })
);

// ==========================================
// POST /api/admin/licenses/:key/unbind-hwid
// ==========================================
/**
 * Clears the machine binding so the next verify-license rebinds to whichever
 * station activates it.
 *
 * It does NOT end a session that is already running on the old machine — that
 * station keeps its access token until it expires, which is the correct
 * behaviour when the customer is mid-shift on the machine being replaced.
 */
router.post(
    "/licenses/:key/unbind-hwid",
    asyncRoute(async (req, res) => {
        const loaded = await loadLicense(req, res);
        if (!loaded) return undefined;

        await withTimeout(licenseRef(loaded.key).update({ hwid: "" }), FIREBASE_TIMEOUT_MS, "FIREBASE");

        logEvent("info", "admin.license_hwid_unbound", {
            key: maskLicenseKey(loaded.key),
            hadBinding: Boolean(loaded.record.hwid)
        });

        return res.json({ success: true, key: loaded.key, hwid: "" });
    })
);

// ==========================================
// POST /api/admin/licenses/:key/tier
// ==========================================
/**
 * Moves a key between BASE and ULTRA.
 *
 * Not in the endpoint list of the task, but the dashboard's action menu
 * specifies a "Đổi Tier" item — without this the menu entry would be dead. The
 * tier is re-validated here rather than trusted from the UI, because a tier
 * server.js does not recognise is refused at verify time and reads to the
 * customer as a dead key.
 */
router.post(
    "/licenses/:key/tier",
    asyncRoute(async (req, res) => {
        const loaded = await loadLicense(req, res);
        if (!loaded) return undefined;

        const tier = normalizeTier(req.body?.tier);
        if (!KNOWN_TIERS.has(tier)) {
            return fail(res, 400, "INVALID_TIER", "Tier chỉ nhận BASE hoặc ULTRA.");
        }

        await withTimeout(licenseRef(loaded.key).update({ tier }), FIREBASE_TIMEOUT_MS, "FIREBASE");

        logEvent("info", "admin.license_tier_changed", {
            key: maskLicenseKey(loaded.key),
            from: normalizeTier(loaded.record.tier) || "unknown",
            to: tier
        });

        return res.json({ success: true, key: loaded.key, tier });
    })
);

module.exports = router;

// Exported for the tests and for server.js's boot banner. The token itself is
// never exported — only whether one was accepted, and why it was not.
module.exports.adminApiEnabled = Boolean(ADMIN_TOKEN);
module.exports.adminDisabledReason = ADMIN_DISABLED_REASON;
module.exports.minAdminTokenLength = MIN_ADMIN_TOKEN_LENGTH;

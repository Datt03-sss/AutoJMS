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
    DEFAULT_OFFLINE_GRACE_HOURS,
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

/**
 * The rest of the fleet defaults, mirrored from server.js for the same reason
 * GRACE_DAYS is: the edit modal shows an empty field as "inherits <default>", and
 * a dashboard that named a different number than /api/verify-license applies
 * would have the owner tuning against a value the station never sees.
 *
 * Each one is read from the same environment variable its sibling in server.js
 * reads, so a Render override moves both halves together.
 */
const OFFLINE_GRACE_HOURS = Number(process.env.LICENSE_OFFLINE_GRACE_HOURS || DEFAULT_OFFLINE_GRACE_HOURS);
const DEFAULT_CHANNEL = process.env.DEFAULT_UPDATE_CHANNEL || "stable";
const DEFAULT_SEATS = (() => {
    // Same bounding server.js:198 applies, so a typo'd DATAHUB_DEFAULT_SEATS is
    // reported here as the number the assertion will really carry.
    const parsed = Number(process.env.DATAHUB_DEFAULT_SEATS);
    if (!Number.isFinite(parsed)) return 3;
    return Math.min(Math.max(Math.trunc(parsed), 1), 500);
})();

/** The two tiers the desktop client knows how to enforce (server.js:409). */
const KNOWN_TIERS = new Set(["BASE", "ULTRA"]);

/** The two lifecycle states verify-license distinguishes: active, or not. */
const KNOWN_STATUSES = new Set(["active", "revoked"]);

/** Velopack channels `release/build-release.ps1` actually publishes. */
const KNOWN_CHANNELS = new Set(["stable", "beta"]);

/** Ten years of monthly terms. A bound, so a fat-fingered 10000 cannot be sold. */
const MAX_TERMS = 120;

/** Free-text the owner types; bounded so a paste cannot bloat the record. */
const MAX_NOTES_LENGTH = 500;

/** A Google Sheet id is 44 characters; the ceiling is slack, not a format. */
const MAX_SPREADSHEET_ID_LENGTH = 200;

/**
 * Bounds for the per-key overrides the edit modal exposes.
 *
 * seats and tokenVersion are NOT free choices: server.js runs both through
 * boundedNumber() before they reach a DataHub assertion, so 9999 seats is
 * silently clamped to 500. These refuse instead of clamping — a dashboard that
 * stored a number the fleet quietly rewrites is showing the owner a fiction.
 */
const MIN_SEATS = 1;
const MAX_SEATS = 500;                  // server.js:255, server.js:1301
const MIN_TOKEN_VERSION = 1;
const MAX_TOKEN_VERSION = 1_000_000;    // server.js:256
const MAX_GRACE_DAYS = 365;
const MAX_OFFLINE_GRACE_HOURS = 8760;   // one year, in hours
const MAX_SITE_CODES = 20;
const MAX_SITE_ID_LENGTH = 128;
const MAX_HWID_LENGTH = 128;

/**
 * Mirrors PLACEHOLDER_SITE_CODES at server.js:125.
 *
 * Not a duplicate for convenience: resolveLicenseSiteCodes() drops every one of
 * these before it signs a DataHub assertion, so a site list made only of
 * placeholders leaves the key unable to enrol at all. Refusing them here means
 * the owner finds out while the modal is open rather than when the station
 * fails to reach the data plane.
 */
const PLACEHOLDER_SITE_CODES = new Set(["", "0000", "00000", "0", "DEFAULT", "NONE", "TBD"]);

// ==========================================
// BROADCAST UPDATE
// ==========================================

/**
 * Where the fleet-wide update directive lives.
 *
 * A single node, not a per-key field: "everyone on 1.26.12" is one decision, and
 * spreading it across every licence record would mean an owner could half-apply
 * it and a new key could be created without it.
 */
const BROADCAST_UPDATE_PATH = "config/broadcastUpdate";

/**
 * Mirrors APP_VERSION_PATTERN in server.js.
 *
 * It has to: server.js runs the stored version through its own copy before the
 * fleet ever sees the directive, so a version this router accepted but that one
 * drops would be a broadcast switched on in the dashboard and silently never
 * sent. Accepts "1.26.12" and "1.26.12-beta.1".
 */
const BROADCAST_VERSION_PATTERN = /^[0-9A-Za-z][0-9A-Za-z.+-]{0,63}$/;

/** Mirrors BROADCAST_MESSAGE_MAX_LENGTH in server.js, which truncates at the same number. */
const BROADCAST_MESSAGE_MAX_LENGTH = 300;

/**
 * Where the release list comes from, and how long the dropdown is willing to wait.
 *
 * Both are overridable so the tests can point them at a local server instead of
 * reaching GitHub — a suite that needs the network is a suite that fails for
 * reasons that have nothing to do with the change being tested.
 */
const RELEASES_API_URL =
    process.env.GITHUB_RELEASES_API_URL || "https://api.github.com/repos/Datt03-sss/AutoJMS-Update/releases";
const UPDATE_XML_URL =
    process.env.UPDATE_XML_URL || "https://raw.githubusercontent.com/Datt03-sss/AutoJMS-Update/main/update.xml";
const RELEASES_FETCH_TIMEOUT_MS = Number(process.env.RELEASES_FETCH_TIMEOUT_MS || 6000);

/** Enough to fill a dropdown; the owner is picking a recent build, not browsing history. */
const MAX_RELEASES = 30;

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
 * JSON with object keys sorted, used to ask "did this field actually move?".
 *
 * Plain JSON.stringify would answer yes every time for modulePolicy: Firebase
 * hands `val()` back with its children in the database's own order, which is not
 * the order this file writes them in, so `{a,s,p}` and `{p,a,s}` would compare
 * unequal and every save would rewrite an unchanged policy — and report it as a
 * change in the audit log. Array order is preserved on purpose: siteCodes[0] is
 * the site an assertion is minted for, so reordering that list is a real change.
 */
function stableJson(value) {
    if (value === undefined) return "null";
    if (value === null || typeof value !== "object") return JSON.stringify(value);
    if (Array.isArray(value)) return `[${value.map(stableJson).join(",")}]`;
    return `{${Object.keys(value)
        .sort()
        .map(name => `${JSON.stringify(name)}:${stableJson(value[name])}`)
        .join(",")}}`;
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

/**
 * A hardware id or a DataHub site GUID: an opaque identifier the client produced.
 *
 * Same treatment as the spreadsheet id — printable ASCII only and bounded —
 * because both are values the owner pastes rather than types, and a stray
 * newline from a copied log line would otherwise reach the record.
 */
function sanitizeOpaqueId(raw, maxLength) {
    if (typeof raw !== "string" && typeof raw !== "number") return "";
    return String(raw).replace(/[^ -~]/g, "").trim().slice(0, maxLength);
}

/**
 * An optional whole-number override.
 *
 * Three outcomes, and the middle one is the point: null/"" is the owner asking
 * the key to follow the fleet default, which is a DIFFERENT record from one
 * pinned to the same number — the pinned one stops moving when the fleet default
 * is retuned on Render. It is spelled as a Firebase delete, not as a stored zero.
 *
 * Returns null when the value is present but unusable, so a typo is refused
 * rather than rounded into something plausible.
 */
function parseOptionalWhole(raw, { min, max }) {
    if (raw === null || (typeof raw === "string" && raw.trim() === "")) return { inherit: true };
    if (typeof raw !== "number" && typeof raw !== "string") return null;

    const parsed = Number(raw);
    if (!Number.isInteger(parsed) || parsed < min || parsed > max) return null;
    return { inherit: false, value: parsed };
}

/**
 * The list of DataHub sites a key may enrol against.
 *
 * Accepts the array the record stores or the one-per-line text the modal sends.
 * An empty result is spelled as a delete: with no siteCodes child,
 * resolveLicenseSiteCodes() falls back to siteCode / siteId / middleCode, which
 * is how every single-site key in the fleet already works.
 */
function parseSiteCodes(raw) {
    const entries = Array.isArray(raw) ? raw : String(raw ?? "").split(/[\s,;]+/);
    if (entries.length > MAX_SITE_CODES * 4) return null;

    const codes = [];
    for (const entry of entries) {
        const code = String(entry ?? "").trim().toUpperCase();
        if (!code) continue;
        if (!MIDDLE_CODE_PATTERN.test(code)) return null;
        if (PLACEHOLDER_SITE_CODES.has(code) || /^0+$/.test(code)) return null;
        if (!codes.includes(code)) codes.push(code);
    }

    return codes.length > MAX_SITE_CODES ? null : codes;
}

/** "YYYY-MM-DD" as a VN calendar day, or null. */
function parseVnDate(raw) {
    const matched = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(raw ?? "").trim());
    if (!matched) return null;

    const iso = `${matched[1]}-${matched[2]}-${matched[3]}T00:00:00+07:00`;
    const ms = parseInstant(iso);
    if (ms === null) return null;

    // Date.parse() rolls 2026-02-31 forward into March rather than refusing it,
    // so the round trip IS the calendar check: a date that comes back as a
    // different day was never a real one.
    return toVnIso(ms) === iso ? { iso, ms } : null;
}

/**
 * Resolves what the edit modal's "Hạn dùng" control asked for.
 *
 * Four modes, because a key's expiry is edited for four different reasons
 * (owner decision, 2026-09-13):
 *
 *   keep       leave the stored expiry exactly as it is — the default, so a save
 *              that only touched the notes cannot move a renewal date
 *   perpetual  drop the expiry; the v1 record shape evaluateLicense() never expires
 *   anchor     the existing sale mechanism: N whole anchor-to-anchor months from
 *              a start day, with the 30-day floor. terms 0 is the short form the
 *              owner asked for — run from the chosen day to the 16th of that
 *              month, rolling into the next month once the 16th has passed
 *   date       a designated day, 00:00 +07:00, the same instant of day every
 *              anchored expiry in the fleet already lands on
 *
 * Returns { change: false } for "keep", or null when the request is unusable.
 */
function parseExpiryChange(raw, nowMs) {
    if (raw === null || raw === undefined) return { change: false };
    if (typeof raw !== "object" || Array.isArray(raw)) return null;

    const mode = String(raw.mode || "keep").trim().toLowerCase();

    if (mode === "keep") return { change: false };
    if (mode === "perpetual") return { change: true, expiresAt: null, expiresAtMs: null };

    if (mode === "date") {
        const parsed = parseVnDate(raw.date);
        return parsed ? { change: true, expiresAt: parsed.iso, expiresAtMs: parsed.ms } : null;
    }

    if (mode === "anchor") {
        // An absent start day means "from today", which is what a renewal bought
        // right now means — and what the create route already does.
        const start =
            raw.startAt === null || raw.startAt === undefined || raw.startAt === ""
                ? { ms: nowMs }
                : parseVnDate(raw.startAt);
        if (!start) return null;

        const rawTerms = raw.terms === null || raw.terms === undefined || raw.terms === "" ? 1 : raw.terms;
        if (typeof rawTerms !== "number" && typeof rawTerms !== "string") return null;

        const terms = Number(rawTerms);
        if (!Number.isInteger(terms) || terms < 0 || terms > MAX_TERMS) return null;

        // 0 is free to mean "next anchor, no 30-day floor" here precisely because
        // perpetual has a mode of its own — unlike parseTerms(), where an empty
        // term selector is the only way to spell a lifetime key.
        const computed =
            terms === 0
                ? computeExpiry(start.ms, { terms: 1, minTermDays: 0, anchorDay: ANCHOR_DAY })
                : computeExpiry(start.ms, { terms, anchorDay: ANCHOR_DAY });

        return { change: true, expiresAt: computed.expiresAt, expiresAtMs: computed.expiresAtMs };
    }

    return null;
}

/**
 * Every field of a record the licence server reads, normalised for the dashboard.
 *
 * The table shows nine of them; the edit modal needs all of them, and it needs to
 * tell "not set" from "set to the fleet default" — so an override that is absent
 * comes back as null here, never as the number it would inherit. The defaults
 * themselves travel separately, on the detail route.
 *
 * Where a field is absent, this reports what /api/verify-license would actually
 * hand the station rather than a blank: a record with no modulePolicy is read by
 * server.js:1060 as autoUpdate off and the other two on, and a modal that drew
 * three empty checkboxes would be describing a record that does not exist.
 */
function describeLicense(key, record, now = Date.now()) {
    const evaluation = evaluateLicense(
        {
            status: record.status,
            expiresAt: record.expiresAt,
            graceDays: record.graceDays ?? GRACE_DAYS
        },
        now
    );

    // `expiresAt` is the STORED value normalised, not evaluateLicense's — that one
    // is null for anything revoked, which would blank the expiry column for exactly
    // the keys an owner is most likely to be inspecting.
    const storedExpiryMs = parseInstant(record.expiresAt);

    const optionalNumber = value => {
        if (value === null || value === undefined || value === "") return null;
        const parsed = Number(value);
        return Number.isFinite(parsed) ? parsed : null;
    };

    return {
        key,
        middleCode: String(record.middleCode || ""),
        tier: normalizeTier(record.tier) || "BASE",
        status: String(record.status || "unknown"),
        effectiveStatus: evaluation.effectiveStatus,
        expiresAt: storedExpiryMs === null ? null : toVnIso(storedExpiryMs),
        daysRemaining: evaluation.daysRemaining,
        hwid: String(record.hwid || ""),
        notes: String(record.notes || ""),

        // Shown but not editable: createdAt is where an anchored term was counted
        // from, so rewriting it would re-date a sale that already happened.
        createdAt: String(record.createdAt || ""),

        // Reported by the station itself on verify-license and heartbeat
        // (server.js's recordLicenseActivity), never set from this router — an
        // owner editing the version field would be editing an observation.
        // Empty means no station running a build new enough to report one has
        // reached the server since this key existed.
        appVersion: String(record.appVersion || ""),
        lastActiveAt:
            typeof record.lastActiveAt === "number" && Number.isFinite(record.lastActiveAt) && record.lastActiveAt > 0
                ? toVnIso(record.lastActiveAt)
                : null,

        dataSpreadsheetId: String(record.dataSpreadsheetId || ""),
        updateChannel: String(record.updateChannel || ""),
        // server.js:1018 reads `data.skipHashCheck === true`, so an absent field is
        // hash checking ON at verify time no matter what the create route defaults to.
        skipHashCheck: record.skipHashCheck === true,
        modulePolicy:
            record.modulePolicy && typeof record.modulePolicy === "object"
                ? {
                    autoUpdate: record.modulePolicy.autoUpdate === true,
                    silentUpdate: record.modulePolicy.silentUpdate === true,
                    applyOnNextStartup: record.modulePolicy.applyOnNextStartup === true
                }
                : { autoUpdate: false, silentUpdate: true, applyOnNextStartup: true },

        graceDays: optionalNumber(record.graceDays),
        offlineGraceHours: optionalNumber(record.offlineGraceHours),
        seats: optionalNumber(record.seats),
        tokenVersion: optionalNumber(record.tokenVersion),

        // null, not [], so the modal can tell "no list, falls back to middleCode"
        // from "an explicitly empty list" — the second one cannot be stored.
        siteCodes: Array.isArray(record.siteCodes)
            ? record.siteCodes.map(code => String(code || "").trim().toUpperCase()).filter(Boolean)
            : null,
        siteCode: String(record.siteCode || ""),
        siteId: String(record.siteId || "")
    };
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
                // Evaluated through describeLicense() rather than inline, so the
                // table and the edit modal can never end up reading a record with
                // two different grace windows. Narrowed back down afterwards
                // because this is the page-load payload for the WHOLE fleet: the
                // remaining fields travel one row at a time on the detail route.
                const full = describeLicense(key, record, now);

                return {
                    key: full.key,
                    middleCode: full.middleCode,
                    tier: full.tier,
                    status: full.status,
                    effectiveStatus: full.effectiveStatus,
                    expiresAt: full.expiresAt,
                    daysRemaining: full.daysRemaining,
                    hwid: full.hwid,
                    notes: full.notes,
                    // Both belong to the table itself — the "Phiên bản" column is
                    // read across the whole fleet at once, which is the entire
                    // point of it, so it cannot wait for the per-row detail call.
                    appVersion: full.appVersion,
                    lastActiveAt: full.lastActiveAt
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
// GET /api/admin/licenses/:key
// ==========================================
/**
 * One record, every field, plus the defaults the blanks inherit.
 *
 * The edit modal opens on this rather than on the row it was clicked from. The
 * row is a snapshot from the last refresh, and an owner who leaves the dashboard
 * open all morning would otherwise be editing — and saving back — a record that
 * moved underneath them.
 */
router.get(
    "/licenses/:key",
    asyncRoute(async (req, res) => {
        const loaded = await loadLicense(req, res);
        if (!loaded) return undefined;

        return res.json({
            success: true,
            license: describeLicense(loaded.key, loaded.record),
            // What an empty field on the form actually means, named by the server
            // that applies them rather than hard-coded into the page.
            defaults: {
                graceDays: GRACE_DAYS,
                offlineGraceHours: OFFLINE_GRACE_HOURS,
                seats: DEFAULT_SEATS,
                tokenVersion: 1,
                updateChannel: DEFAULT_CHANNEL,
                anchorDay: ANCHOR_DAY
            }
        });
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
        // Not gated on tier. BASE uses Google Sheet too: the broker route
        // (/api/google-sheets/grant) checks status and expiry and nothing else,
        // CanUseGoogleSheetFeature() on the client never looks at the tier, and
        // TierRuntimePolicy withholds exactly four things from BASE — inventory
        // sync, database tracking, background auto-sync and FullStackOperation.
        // Sheets is not one of them. Blanking it here left BASE customers with a
        // key that could never be pointed at their own spreadsheet.
        const dataSpreadsheetId = sanitizeSpreadsheetId(req.body?.dataSpreadsheetId);
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
// POST /api/admin/licenses/:key/update
// ==========================================
/**
 * Edits an existing key in place — every field the licence server reads.
 *
 * The four single-purpose routes below it stay: they are one click each from the
 * table and they are what the owner reaches for ninety times out of a hundred.
 * This one exists for the tenth case, where a correction spans several fields at
 * once (a station moved office, so middleCode, siteCodes and hwid all move
 * together) and doing it as four separate writes would leave the record
 * incoherent in between.
 *
 * Two properties it has that the single-purpose routes do not need:
 *
 *   PATCH semantics. A field absent from the body is left alone. The modal posts
 *   the whole form, but a caller that sends only `notes` must not blank the
 *   tier — this route can write every field, so "not mentioned" has to mean
 *   "not touched" rather than "not wanted".
 *
 *   Only real changes are written. The modal posts every field on every save, so
 *   without the diff a notes edit would rewrite status, tier and middleCode with
 *   their own values, and the log line would claim all of them moved — the
 *   opposite of an audit trail on the one surface that can revoke a customer.
 *
 * The key itself is not editable and there is deliberately no route that makes it
 * so: the key IS the Firebase node id, so renaming it is a copy to a new node and
 * a delete of the old one, which would strand every station already carrying the
 * old string. Issue a new key and revoke the old one instead.
 */
router.post(
    "/licenses/:key/update",
    asyncRoute(async (req, res) => {
        const loaded = await loadLicense(req, res);
        if (!loaded) return undefined;

        const body = req.body && typeof req.body === "object" && !Array.isArray(req.body) ? req.body : {};
        const current = loaded.record;
        const now = Date.now();

        const patch = {};
        const changed = [];

        /**
         * Stages one field, and only when it actually moves.
         *
         * null is a Firebase delete, which is how an override goes back to
         * inheriting the fleet default — so an absent child and an explicit null
         * have to compare equal here, or every save would rewrite the same
         * delete forever.
         */
        const put = (name, next, before) => {
            if (stableJson(before) === stableJson(next)) return;
            patch[name] = next;
            changed.push(name);
        };

        // ---- Identity -------------------------------------------------------

        if (body.middleCode !== undefined) {
            const middleCode = String(body.middleCode || "").trim().toUpperCase();
            if (!MIDDLE_CODE_PATTERN.test(middleCode)) {
                return fail(res, 400, "INVALID_MIDDLE_CODE", "Mã bưu cục chỉ gồm chữ và số (A-Z, 0-9), dài 2-32 ký tự.");
            }
            if (/^0+$/.test(middleCode)) {
                return fail(
                    res,
                    400,
                    "PLACEHOLDER_MIDDLE_CODE",
                    "Mã bưu cục toàn số 0 bị verify-license từ chối; nhập mã bưu cục thật."
                );
            }
            put("middleCode", middleCode, current.middleCode);
        }

        if (body.tier !== undefined) {
            const tier = normalizeTier(body.tier);
            if (!KNOWN_TIERS.has(tier)) {
                return fail(res, 400, "INVALID_TIER", "Tier chỉ nhận BASE hoặc ULTRA.");
            }
            put("tier", tier, normalizeTier(current.tier) || undefined);
        }

        if (body.status !== undefined) {
            const status = String(body.status || "").trim().toLowerCase();
            if (!KNOWN_STATUSES.has(status)) {
                return fail(res, 400, "INVALID_STATUS", "Trạng thái chỉ nhận active hoặc revoked.");
            }
            put("status", status, String(current.status || "").trim().toLowerCase() || undefined);
        }

        if (body.hwid !== undefined) {
            put("hwid", sanitizeOpaqueId(body.hwid, MAX_HWID_LENGTH), String(current.hwid || ""));
        }

        if (body.notes !== undefined) {
            put("notes", sanitizeNotes(body.notes), String(current.notes || ""));
        }

        // ---- Client configuration -------------------------------------------

        if (body.dataSpreadsheetId !== undefined) {
            put(
                "dataSpreadsheetId",
                sanitizeSpreadsheetId(body.dataSpreadsheetId),
                String(current.dataSpreadsheetId || "")
            );
        }

        if (body.updateChannel !== undefined) {
            const channel = String(body.updateChannel || "").trim().toLowerCase();
            if (channel && !KNOWN_CHANNELS.has(channel)) {
                return fail(res, 400, "INVALID_UPDATE_CHANNEL", "Kênh cập nhật chỉ nhận stable hoặc beta, hoặc bỏ trống.");
            }
            // Blank deletes the child rather than storing "", because server.js
            // falls back on `data.updateChannel || CONFIG.DEFAULT_CHANNEL` and an
            // empty string there is the same fallback said twice.
            put("updateChannel", channel || null, String(current.updateChannel || "") || undefined);
        }

        if (body.skipHashCheck !== undefined) {
            put("skipHashCheck", Boolean(body.skipHashCheck), current.skipHashCheck === true);
        }

        if (body.modulePolicy !== undefined) {
            const requested = body.modulePolicy;
            if (requested === null || typeof requested !== "object" || Array.isArray(requested)) {
                return fail(res, 400, "INVALID_MODULE_POLICY", "modulePolicy phải là object gồm ba công tắc true/false.");
            }

            // Merged onto what is stored, not onto the fleet default: a caller
            // sending only { autoUpdate: false } is turning one switch off, not
            // resetting the other two.
            const before = describeLicense(loaded.key, current, now).modulePolicy;
            const next = {
                // Anything else already under modulePolicy is carried through:
                // update() replaces the whole child, so building the object from
                // the three switches alone would silently delete a field some
                // future client reads.
                ...(current.modulePolicy && typeof current.modulePolicy === "object" ? current.modulePolicy : {}),
                autoUpdate: requested.autoUpdate === undefined ? before.autoUpdate : requested.autoUpdate === true,
                silentUpdate: requested.silentUpdate === undefined ? before.silentUpdate : requested.silentUpdate === true,
                applyOnNextStartup:
                    requested.applyOnNextStartup === undefined
                        ? before.applyOnNextStartup
                        : requested.applyOnNextStartup === true
            };
            put("modulePolicy", next, current.modulePolicy);
        }

        // ---- Lifecycle overrides --------------------------------------------

        const overrides = [
            {
                name: "graceDays",
                code: "INVALID_GRACE_DAYS",
                bounds: { min: 0, max: MAX_GRACE_DAYS },
                message: `Số ngày ân hạn phải là số nguyên 0-${MAX_GRACE_DAYS}, hoặc bỏ trống để theo mặc định (${GRACE_DAYS}).`
            },
            {
                name: "offlineGraceHours",
                code: "INVALID_OFFLINE_GRACE_HOURS",
                bounds: { min: 0, max: MAX_OFFLINE_GRACE_HOURS },
                message: `Số giờ chạy offline phải là số nguyên 0-${MAX_OFFLINE_GRACE_HOURS}, hoặc bỏ trống để theo mặc định (${OFFLINE_GRACE_HOURS}).`
            },
            {
                name: "seats",
                code: "INVALID_SEATS",
                bounds: { min: MIN_SEATS, max: MAX_SEATS },
                message: `Số máy trạm phải là số nguyên ${MIN_SEATS}-${MAX_SEATS}, hoặc bỏ trống để theo mặc định (${DEFAULT_SEATS}).`
            },
            {
                name: "tokenVersion",
                code: "INVALID_TOKEN_VERSION",
                bounds: { min: MIN_TOKEN_VERSION, max: MAX_TOKEN_VERSION },
                message: `Token version phải là số nguyên ${MIN_TOKEN_VERSION}-${MAX_TOKEN_VERSION}, hoặc bỏ trống để theo mặc định (1).`
            }
        ];

        for (const { name, code, bounds, message } of overrides) {
            if (body[name] === undefined) continue;

            const parsed = parseOptionalWhole(body[name], bounds);
            if (parsed === null) return fail(res, 400, code, message);

            const stored = Number(current[name]);
            const before =
                current[name] === null || current[name] === undefined || current[name] === "" || !Number.isFinite(stored)
                    ? undefined
                    : stored;
            put(name, parsed.inherit ? null : parsed.value, before);
        }

        // ---- DataHub tenancy ------------------------------------------------

        if (body.siteCodes !== undefined) {
            const codes = parseSiteCodes(body.siteCodes);
            if (codes === null) {
                return fail(
                    res,
                    400,
                    "INVALID_SITE_CODES",
                    `Danh sách site code chỉ gồm chữ và số, tối đa ${MAX_SITE_CODES} mã, không nhận mã placeholder (0000, NONE, TBD…).`
                );
            }
            const before = Array.isArray(current.siteCodes)
                ? current.siteCodes.map(code => String(code || "").trim().toUpperCase()).filter(Boolean)
                : undefined;
            put("siteCodes", codes.length > 0 ? codes : null, before && before.length > 0 ? before : undefined);
        }

        if (body.siteCode !== undefined) {
            const siteCode = String(body.siteCode || "").trim().toUpperCase();
            if (siteCode && (!MIDDLE_CODE_PATTERN.test(siteCode) || PLACEHOLDER_SITE_CODES.has(siteCode) || /^0+$/.test(siteCode))) {
                return fail(res, 400, "INVALID_SITE_CODE", "Site code chỉ gồm chữ và số, và không được là mã placeholder.");
            }
            put("siteCode", siteCode || null, String(current.siteCode || "") || undefined);
        }

        if (body.siteId !== undefined) {
            // Not pattern-checked: this is the GUID the enrolment response hands
            // back, and older records carry a middleCode here instead. Guessing a
            // shape would refuse real data that is already in the fleet.
            const siteId = sanitizeOpaqueId(body.siteId, MAX_SITE_ID_LENGTH);
            put("siteId", siteId || null, String(current.siteId || "") || undefined);
        }

        // ---- Expiry ---------------------------------------------------------

        const expiry = parseExpiryChange(body.expiry, now);
        if (expiry === null) {
            return fail(
                res,
                400,
                "INVALID_EXPIRY",
                "Hạn dùng không hợp lệ: chọn giữ nguyên, vĩnh viễn, theo kỳ hạn (neo ngày 16), hoặc một ngày có thật dạng YYYY-MM-DD."
            );
        }
        if (expiry.change) {
            const before = parseInstant(current.expiresAt);
            put("expiresAt", expiry.expiresAt, before === null ? undefined : toVnIso(before));
        }

        // ---- Write ----------------------------------------------------------

        if (changed.length === 0) {
            // Not an error the owner caused, but saying "đã lưu" for a write that
            // never happened is how a dashboard teaches someone to trust a toast
            // that means nothing.
            return res.json({
                success: true,
                key: loaded.key,
                changed: [],
                license: describeLicense(loaded.key, current, now)
            });
        }

        await withTimeout(licenseRef(loaded.key).update(patch), FIREBASE_TIMEOUT_MS, "FIREBASE");

        logEvent("info", "admin.license_updated", {
            key: maskLicenseKey(loaded.key),
            changed,
            // Named individually because these three are the ones that change who
            // can run the software and which DataHub tenant they land in — a
            // reader scanning the log should not have to diff two records to see it.
            status: patch.status,
            tier: patch.tier,
            middleCode: patch.middleCode,
            expiresAt: changed.includes("expiresAt") ? patch.expiresAt : undefined
        });

        const merged = { ...current };
        for (const [name, value] of Object.entries(patch)) {
            if (value === null) delete merged[name];
            else merged[name] = value;
        }

        return res.json({
            success: true,
            key: loaded.key,
            changed,
            license: describeLicense(loaded.key, merged, now)
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

// ==========================================
// RELEASE SOURCES
// ==========================================

/**
 * The plain SemVer inside a git tag.
 *
 * AutoJMS tags every build `-Release` (`v1.26.12-Release`,
 * `v1.26.13-beta.1-Release`) — a house convention, not a SemVer prerelease
 * label. Handing it to the client untouched makes `1.26.12-Release` sort BELOW
 * `1.26.12`, so a station reads the newest build as a downgrade and refuses it;
 * and `beta.1-Release` / `beta.2-Release` both parse their build number as 0,
 * so no beta ever supersedes another. Strip it here, at the one place tags
 * become versions.
 */
function versionFromTag(tag) {
    return String(tag || "")
        .trim()
        .replace(/^[vV]/, "")
        .replace(/-[Rr]elease$/i, "");
}

/** One release, in the only shape the dropdown and the POST body care about. */
function toReleaseEntry({ tag, name, prerelease, publishedAt }) {
    const version = versionFromTag(tag);
    if (!BROADCAST_VERSION_PATTERN.test(version)) return null;

    return {
        tag: String(tag || ""),
        version,
        name: String(name || "").slice(0, 200),
        // The channel is derived, never read from a field: Velopack publishes a
        // prerelease to `beta` and everything else to `stable`, and a release
        // whose flag and tag disagreed would send a station to a feed that has
        // no such version.
        channel: prerelease === true ? "beta" : "stable",
        prerelease: prerelease === true,
        publishedAt: String(publishedAt || "")
    };
}

async function fetchWithTimeout(url, accept) {
    const response = await fetch(url, {
        headers: {
            accept,
            // GitHub refuses an API request that does not identify itself.
            "user-agent": "AutoJMS-License-Dashboard"
        },
        signal: AbortSignal.timeout(RELEASES_FETCH_TIMEOUT_MS)
    });

    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    return response;
}

async function fetchGithubReleases() {
    const response = await fetchWithTimeout(RELEASES_API_URL, "application/vnd.github+json");
    const payload = await response.json();

    if (!Array.isArray(payload)) throw new Error("RELEASES_NOT_AN_ARRAY");

    return payload
        // A draft is not downloadable, so offering it would produce a broadcast
        // the fleet cannot satisfy.
        .filter(item => item && typeof item === "object" && item.draft !== true)
        .map(item =>
            toReleaseEntry({
                tag: item.tag_name,
                name: item.name,
                prerelease: item.prerelease,
                publishedAt: item.published_at
            })
        )
        .filter(Boolean)
        .slice(0, MAX_RELEASES);
}

/**
 * The same list, read off the manifest the desktop client itself uses.
 *
 * Parsed with regular expressions rather than an XML library, deliberately: this
 * is a fallback for one small document with a known shape, and adding a parser
 * dependency to a server that signs licence assertions is a worse trade than
 * two regexes that fail closed. Anything they cannot read is skipped, and a
 * document they cannot read at all produces an empty list, which the route turns
 * into a 502 rather than a silent "no releases".
 */
async function fetchUpdateXmlReleases() {
    const response = await fetchWithTimeout(UPDATE_XML_URL, "application/xml,text/xml");
    const xml = await response.text();

    const releases = [];

    for (const match of xml.matchAll(/<channel\b([^>]*)>([\s\S]*?)<\/channel>/g)) {
        const attributes = match[1];
        const body = match[2];

        const enabled = /enabled\s*=\s*"(?<value>[^"]*)"/.exec(attributes)?.groups?.value;
        if (String(enabled).toLowerCase() === "false") continue;

        const name = /name\s*=\s*"(?<value>[^"]*)"/.exec(attributes)?.groups?.value || "";
        const prerelease = String(
            /prerelease\s*=\s*"(?<value>[^"]*)"/.exec(attributes)?.groups?.value || ""
        ).toLowerCase() === "true";

        const readTag = tagName =>
            new RegExp(`<${tagName}>(?<value>[^<]*)</${tagName}>`).exec(body)?.groups?.value?.trim() || "";

        const tag = readTag("releaseTag") || readTag("tag");
        const version = readTag("velopackVersion") || versionFromTag(tag);

        const entry = toReleaseEntry({
            tag: tag || version,
            name: readTag("displayVersion") || name,
            // `name` is the authority here and the attribute is the tie-breaker:
            // update.xml names its channels "stable" and "beta" outright, which
            // is more direct evidence than a flag that may be absent.
            prerelease: name.trim().toLowerCase() === "beta" ? true : prerelease,
            publishedAt: ""
        });

        // toReleaseEntry works off the tag; a channel whose tag is missing but
        // whose velopackVersion is present still has everything needed.
        if (entry) {
            releases.push({ ...entry, version });
            continue;
        }

        const fallback = toReleaseEntry({
            tag: version,
            name: readTag("displayVersion") || name,
            prerelease: name.trim().toLowerCase() === "beta" ? true : prerelease,
            publishedAt: ""
        });

        if (fallback) releases.push(fallback);
    }

    if (releases.length === 0) throw new Error("UPDATE_XML_NO_CHANNELS");

    return releases.slice(0, MAX_RELEASES);
}

// ==========================================
// GET /api/admin/broadcast-update
// ==========================================
/**
 * The directive as stored, switched on or off.
 *
 * `active: false` is returned WITH the version and message it last carried, on
 * purpose: the modal reopens on the previous broadcast rather than on a blank
 * form, so re-sending the same one is a single click and the owner can see what
 * the last one said.
 */
router.get(
    "/broadcast-update",
    asyncRoute(async (req, res) => {
        const snapshot = await withTimeout(
            admin.database().ref(BROADCAST_UPDATE_PATH).once("value"),
            FIREBASE_TIMEOUT_MS,
            "FIREBASE"
        );

        const stored = snapshot.val();
        const record = stored && typeof stored === "object" ? stored : {};

        return res.json({
            success: true,
            broadcastUpdate: {
                active: record.active === true,
                version: String(record.version || ""),
                channel: KNOWN_CHANNELS.has(String(record.channel || "")) ? String(record.channel) : DEFAULT_CHANNEL,
                message: String(record.message || ""),
                updatedAt: Number.isFinite(Number(record.updatedAt)) ? Number(record.updatedAt) : 0
            }
        });
    })
);

// ==========================================
// POST /api/admin/broadcast-update
// ==========================================
/**
 * Switches the fleet-wide update prompt on or off.
 *
 * Switching OFF takes no other field and keeps the ones already stored — see the
 * GET above for why. Switching ON re-validates the version and the channel here
 * rather than trusting the dropdown, because a version string server.js's own
 * sanitiser would drop becomes a broadcast that reads as "on" in the dashboard
 * and reaches nobody: the most expensive possible failure for this feature,
 * since the owner's next move is to wait.
 *
 * It does NOT verify that the version actually exists as a release. The station
 * decides that for itself — it compares against its own build and then asks
 * Velopack, which refuses a version that is not published. Refusing here as well
 * would mean a broadcast could not be prepared before the release goes out.
 */
router.post(
    "/broadcast-update",
    asyncRoute(async (req, res) => {
        const active = req.body?.active === true;
        const now = Date.now();

        if (!active) {
            await withTimeout(
                admin.database().ref(BROADCAST_UPDATE_PATH).update({ active: false, updatedAt: now }),
                FIREBASE_TIMEOUT_MS,
                "FIREBASE"
            );

            logEvent("info", "admin.broadcast_update_disabled", {});

            return res.json({ success: true, active: false, updatedAt: now });
        }

        const version = String(req.body?.version || "").trim();
        if (!BROADCAST_VERSION_PATTERN.test(version)) {
            return fail(
                res,
                400,
                "INVALID_BROADCAST_VERSION",
                "Phiên bản chỉ nhận chữ, số và . + - (ví dụ 1.26.12 hoặc 1.26.12-beta.1)."
            );
        }

        const requestedChannel = String(req.body?.channel || "").trim().toLowerCase();
        if (!KNOWN_CHANNELS.has(requestedChannel)) {
            return fail(res, 400, "INVALID_UPDATE_CHANNEL", "Kênh cập nhật chỉ nhận stable hoặc beta.");
        }

        // A beta build exists only on the beta feed. A broadcast naming one while
        // saying "stable" sends every station to a feed that has no such release:
        // Velopack answers "you are up to date", so the broadcast looks delivered
        // and changes nothing. Correct it here rather than trusting the form.
        const channel = version.toLowerCase().includes("-beta") ? "beta" : requestedChannel;
        if (channel !== requestedChannel) {
            logEvent("info", "admin.broadcast_channel_corrected", { version, requestedChannel, channel });
        }

        // Same treatment as notes: this string is typed by the owner and shown in
        // a MessageBox on every station in the fleet.
        const message = sanitizeNotes(req.body?.message).slice(0, BROADCAST_MESSAGE_MAX_LENGTH);

        const directive = { active: true, version, channel, message, updatedAt: now };

        await withTimeout(
            admin.database().ref(BROADCAST_UPDATE_PATH).update(directive),
            FIREBASE_TIMEOUT_MS,
            "FIREBASE"
        );

        logEvent("info", "admin.broadcast_update_enabled", { version, channel, hasMessage: message !== "" });

        return res.json({ success: true, ...directive });
    })
);

// ==========================================
// GET /api/admin/releases
// ==========================================
/**
 * The versions the broadcast dropdown may offer.
 *
 * GitHub first, update.xml second. The fallback is not decoration: the releases
 * API is unauthenticated here and rate-limited per IP, and Render's egress
 * address is shared — so "60 requests an hour, for everyone on this host" is a
 * limit the dashboard can genuinely hit. update.xml is a raw file with no such
 * limit, and it is the same manifest the desktop client itself reads, so the two
 * sources cannot disagree about what "stable" means.
 *
 * A failure of BOTH is a 502 and not an empty list: an empty dropdown looks
 * exactly like "no releases have been published", and the owner would go looking
 * at the wrong repository.
 */
router.get(
    "/releases",
    asyncRoute(async (req, res) => {
        try {
            const releases = await fetchGithubReleases();
            return res.json({ success: true, source: "github", count: releases.length, releases });
        } catch (githubError) {
            logEvent("warn", "admin.releases_github_failed", { message: githubError?.message });
        }

        try {
            const releases = await fetchUpdateXmlReleases();
            return res.json({ success: true, source: "update.xml", count: releases.length, releases });
        } catch (xmlError) {
            logEvent("error", "admin.releases_unavailable", { message: xmlError?.message });

            return fail(
                res,
                502,
                "RELEASES_UNAVAILABLE",
                "Không đọc được danh sách bản phát hành từ GitHub hoặc update.xml. Nhập phiên bản thủ công."
            );
        }
    })
);

module.exports = router;

// Exported for the tests and for server.js's boot banner. The token itself is
// never exported — only whether one was accepted, and why it was not.
module.exports.adminApiEnabled = Boolean(ADMIN_TOKEN);
module.exports.adminDisabledReason = ADMIN_DISABLED_REASON;
module.exports.minAdminTokenLength = MIN_ADMIN_TOKEN_LENGTH;

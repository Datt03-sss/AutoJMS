"use strict";

// ==========================================================================
// AutoJMS license lifecycle — billing anchor day 16
// ==========================================================================
// Business rule (owner decision, 2026-08-24):
//
//   A license is sold as a one-month term, but it always expires at
//   00:00 Asia/Ho_Chi_Minh on the 16th of a month:
//
//       expiresAt = the earliest "day 16, 00:00 +07:00" instant that is
//                   not earlier than (start day + MIN_TERM_DAYS)
//
//   So the customer always gets at least 30 days, plus the leftover days
//   needed to reach the next anchor. Every license in the fleet therefore
//   renews on the same calendar day, which is what makes monthly billing
//   reconcilable.
//
// Day granularity: the term floor is counted from VN midnight of the start
// day, not from the exact creation timestamp. A key created 2026-08-17
// 10:00 gets 2026-09-16 (exactly 30 days) instead of 2026-10-16 (60 days) —
// without that flooring, keys created on the 17th of a month would silently
// receive a double term. The trade-off is that a key created late in the
// day gets up to 24h less than a literal 30 * 24h.
//
// This module is deliberately dependency-free and side-effect-free so it can
// be unit tested with `node --test` and reused by any admin tooling that
// issues keys.
// ==========================================================================

const MS_PER_DAY = 86_400_000;

/** Asia/Ho_Chi_Minh is a fixed +07:00 offset — no DST, no historical shifts to worry about. */
const TZ_OFFSET_MINUTES = 420;
const TZ_OFFSET_MS = TZ_OFFSET_MINUTES * 60_000;

/** Day of month the whole fleet expires on. */
const BILLING_ANCHOR_DAY = 16;

/** Minimum days a paid term must cover before the anchor may be applied. */
const MIN_TERM_DAYS = 30;

/** Days after expiresAt during which the app keeps running but warns. */
const DEFAULT_GRACE_DAYS = 7;

/** Hours the client may run without reaching the license server at all. */
const DEFAULT_OFFLINE_GRACE_HOURS = 72;

/**
 * Calendar parts of an instant, as seen in Asia/Ho_Chi_Minh.
 * @param {number} ms epoch milliseconds
 */
function vnParts(ms) {
    const shifted = new Date(ms + TZ_OFFSET_MS);
    return {
        year: shifted.getUTCFullYear(),
        month: shifted.getUTCMonth(), // 0-based
        day: shifted.getUTCDate()
    };
}

/**
 * The epoch ms of 00:00 Asia/Ho_Chi_Minh on the given calendar date.
 * `month` may overflow (12 -> January of year+1), which is what makes the
 * "next anchor" step below a one-liner.
 */
function vnMidnightMs(year, month, day) {
    return Date.UTC(year, month, day, 0, 0, 0, 0) - TZ_OFFSET_MS;
}

/**
 * Parses an ISO-8601 string, an epoch-ms number, or a legacy
 * "DD-MM-YYYY HH:mm" string (the shape the current Firebase records use).
 * Returns epoch ms, or null when the value is unusable.
 *
 * Legacy strings carry no timezone, so they are read as VN local time —
 * that is how they were written by hand.
 */
function parseInstant(value) {
    if (value === null || value === undefined || value === "") return null;

    if (typeof value === "number") {
        return Number.isFinite(value) ? value : null;
    }

    const text = String(value).trim();
    if (!text) return null;

    const legacy = /^(\d{2})-(\d{2})-(\d{4})(?:[ T](\d{2}):(\d{2}))?$/.exec(text);
    if (legacy) {
        const [, dd, mm, yyyy, hh, mi] = legacy;
        return Date.UTC(Number(yyyy), Number(mm) - 1, Number(dd), Number(hh || 0), Number(mi || 0), 0, 0) - TZ_OFFSET_MS;
    }

    const parsed = Date.parse(text);
    return Number.isNaN(parsed) ? null : parsed;
}

/**
 * ISO-8601 with an explicit +07:00 offset, so the stored value is
 * unambiguous no matter which region reads it back.
 */
function toVnIso(ms) {
    const p = vnParts(ms);
    const shifted = new Date(ms + TZ_OFFSET_MS);
    const pad = (n, w = 2) => String(n).padStart(w, "0");
    return (
        `${p.year}-${pad(p.month + 1)}-${pad(p.day)}` +
        `T${pad(shifted.getUTCHours())}:${pad(shifted.getUTCMinutes())}:${pad(shifted.getUTCSeconds())}+07:00`
    );
}

/**
 * Computes the anchored expiry for a term that starts at `startAt`.
 *
 * @param {string|number|Date} startAt when the term begins (createdAt / renewedAt)
 * @param {object} [options]
 * @param {number} [options.anchorDay=16]
 * @param {number} [options.minTermDays=30]
 * @param {number} [options.terms=1] number of consecutive monthly terms to grant
 * @returns {{ expiresAtMs: number, expiresAt: string, termDays: number }}
 */
function computeExpiry(startAt, options = {}) {
    const anchorDay = Number(options.anchorDay ?? BILLING_ANCHOR_DAY);
    const minTermDays = Number(options.minTermDays ?? MIN_TERM_DAYS);
    const terms = Math.max(1, Math.floor(Number(options.terms ?? 1)));

    const startMs = startAt instanceof Date ? startAt.getTime() : parseInstant(startAt);
    if (startMs === null) {
        throw new TypeError("computeExpiry: startAt is not a valid instant");
    }

    // Count the term floor in whole VN days — see the "Day granularity" note above.
    const startDay = vnParts(startMs);
    const startDayMidnight = vnMidnightMs(startDay.year, startDay.month, startDay.day);
    const floorMs = startDayMidnight + minTermDays * MS_PER_DAY;

    const floorParts = vnParts(floorMs);
    let expiresAtMs = vnMidnightMs(floorParts.year, floorParts.month, anchorDay);
    if (expiresAtMs < floorMs) {
        expiresAtMs = vnMidnightMs(floorParts.year, floorParts.month + 1, anchorDay);
    }

    // Extra terms are whole anchor-to-anchor months, so a 3-month sale is
    // still a single expiry on the 16th.
    for (let i = 1; i < terms; i++) {
        const p = vnParts(expiresAtMs);
        expiresAtMs = vnMidnightMs(p.year, p.month + 1, anchorDay);
    }

    return {
        expiresAtMs,
        expiresAt: toVnIso(expiresAtMs),
        termDays: Math.round((expiresAtMs - startDayMidnight) / MS_PER_DAY)
    };
}

/**
 * Derives the effective lifecycle state of a license record.
 *
 * A license with no `expiresAt` is a v1 record and never expires — that is
 * the only way the fleet in the field keeps working after this ships.
 *
 * @param {object} record the Firebase /Licenses/{key} value
 * @param {number} [nowMs]
 * @returns {{ effectiveStatus: string, expiresAt: string|null, expiresAtMs: number|null,
 *             graceUntil: string|null, graceUntilMs: number|null, daysRemaining: number|null,
 *             allowed: boolean }}
 */
function evaluateLicense(record, nowMs = Date.now()) {
    const status = String(record?.status || "").trim().toLowerCase() || "unknown";

    const deny = (effectiveStatus) => ({
        effectiveStatus,
        expiresAt: null,
        expiresAtMs: null,
        graceUntil: null,
        graceUntilMs: null,
        daysRemaining: null,
        allowed: false
    });

    if (status !== "active") {
        return deny(status);
    }

    const expiresAtMs = parseInstant(record?.expiresAt);
    if (expiresAtMs === null) {
        // v1 record: perpetual until an expiry is backfilled.
        return {
            effectiveStatus: "active",
            expiresAt: null,
            expiresAtMs: null,
            graceUntil: null,
            graceUntilMs: null,
            daysRemaining: null,
            allowed: true
        };
    }

    // null / "" / a typo must fall back to the default, not silently become 0 —
    // Number(null) === 0 would otherwise revoke the whole grace window.
    const graceDaysRaw = record?.graceDays;
    const graceDaysNum =
        graceDaysRaw === null || graceDaysRaw === undefined || graceDaysRaw === ""
            ? NaN
            : Number(graceDaysRaw);
    const graceDays = Number.isFinite(graceDaysNum) && graceDaysNum >= 0 ? graceDaysNum : DEFAULT_GRACE_DAYS;
    const graceUntilMs = expiresAtMs + graceDays * MS_PER_DAY;

    const daysRemaining = Math.ceil((expiresAtMs - nowMs) / MS_PER_DAY);

    let effectiveStatus;
    if (nowMs <= expiresAtMs) effectiveStatus = "active";
    else if (nowMs <= graceUntilMs) effectiveStatus = "grace";
    else effectiveStatus = "expired";

    return {
        effectiveStatus,
        expiresAt: toVnIso(expiresAtMs),
        expiresAtMs,
        graceUntil: toVnIso(graceUntilMs),
        graceUntilMs,
        daysRemaining,
        allowed: effectiveStatus !== "expired"
    };
}

module.exports = {
    MS_PER_DAY,
    TZ_OFFSET_MINUTES,
    BILLING_ANCHOR_DAY,
    MIN_TERM_DAYS,
    DEFAULT_GRACE_DAYS,
    DEFAULT_OFFLINE_GRACE_HOURS,
    parseInstant,
    toVnIso,
    computeExpiry,
    evaluateLicense
};

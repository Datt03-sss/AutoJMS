"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const {
    parseInstant,
    toVnIso,
    computeExpiry,
    evaluateLicense,
    DEFAULT_GRACE_DAYS
} = require("../license-expiry");

const vn = (iso) => Date.parse(iso);

// ---------------------------------------------------------------------------
// parseInstant
// ---------------------------------------------------------------------------

test("parseInstant reads the legacy DD-MM-YYYY HH:mm shape as VN local time", () => {
    assert.equal(parseInstant("26-05-2026 01:22"), vn("2026-05-26T01:22:00+07:00"));
    assert.equal(parseInstant("01-01-2026"), vn("2026-01-01T00:00:00+07:00"));
});

test("parseInstant reads ISO-8601 and epoch milliseconds", () => {
    assert.equal(parseInstant("2026-09-16T00:00:00+07:00"), vn("2026-09-16T00:00:00+07:00"));
    assert.equal(parseInstant(1_700_000_000_000), 1_700_000_000_000);
});

test("parseInstant returns null for unusable values", () => {
    for (const bad of [null, undefined, "", "   ", "not-a-date", NaN]) {
        assert.equal(parseInstant(bad), null, `expected null for ${JSON.stringify(bad)}`);
    }
});

test("toVnIso always carries the +07:00 offset", () => {
    assert.equal(toVnIso(vn("2026-09-16T00:00:00+07:00")), "2026-09-16T00:00:00+07:00");
    // Same instant expressed in UTC must round-trip to VN wall clock.
    assert.equal(toVnIso(Date.parse("2026-09-15T17:00:00Z")), "2026-09-16T00:00:00+07:00");
});

// ---------------------------------------------------------------------------
// computeExpiry — the anchor-16 rule
// ---------------------------------------------------------------------------

test("computeExpiry always lands on the 16th at 00:00 +07:00", () => {
    const cases = [
        // startAt                        expiresAt                    termDays
        ["2026-08-01T09:00:00+07:00", "2026-09-16T00:00:00+07:00", 46],
        ["2026-08-15T23:59:00+07:00", "2026-09-16T00:00:00+07:00", 32],
        ["2026-08-16T00:00:00+07:00", "2026-09-16T00:00:00+07:00", 31],
        ["2026-08-17T10:00:00+07:00", "2026-09-16T00:00:00+07:00", 30],
        ["2026-08-18T00:00:00+07:00", "2026-10-16T00:00:00+07:00", 59],
        ["2026-08-24T01:22:00+07:00", "2026-10-16T00:00:00+07:00", 53]
    ];

    for (const [startAt, expected, termDays] of cases) {
        const got = computeExpiry(startAt);
        assert.equal(got.expiresAt, expected, `startAt=${startAt}`);
        assert.equal(got.termDays, termDays, `termDays for startAt=${startAt}`);
        assert.ok(got.termDays >= 30, `term must never be shorter than 30 days (${startAt})`);
    }
});

test("computeExpiry never grants a double term for keys issued on the 17th", () => {
    // Without day-granular flooring, start + 30 * 24h lands at 10:00 on the
    // 16th — past that month's 00:00 anchor — and the key would silently roll
    // to the following month.
    assert.equal(computeExpiry("2026-08-17T10:00:00+07:00").termDays, 30);
    assert.equal(computeExpiry("2026-08-17T23:59:59+07:00").termDays, 30);
});

test("computeExpiry rolls across a year boundary", () => {
    assert.equal(
        computeExpiry("2026-12-20T08:00:00+07:00").expiresAt,
        "2027-02-16T00:00:00+07:00"
    );
});

test("computeExpiry handles a short February", () => {
    assert.equal(
        computeExpiry("2026-01-31T08:00:00+07:00").expiresAt,
        "2026-03-16T00:00:00+07:00"
    );
});

test("computeExpiry with multiple terms adds whole anchor months", () => {
    assert.equal(
        computeExpiry("2026-08-24T01:22:00+07:00", { terms: 3 }).expiresAt,
        "2026-12-16T00:00:00+07:00"
    );
});

test("computeExpiry accepts a legacy createdAt string", () => {
    assert.equal(computeExpiry("24-08-2026 01:22").expiresAt, "2026-10-16T00:00:00+07:00");
});

test("computeExpiry rejects an unusable startAt", () => {
    assert.throws(() => computeExpiry("tomorrow"), TypeError);
});

// ---------------------------------------------------------------------------
// evaluateLicense — effective status
// ---------------------------------------------------------------------------

test("evaluateLicense denies anything that is not status=active", () => {
    for (const status of ["inactive", "locked", "suspended", "", undefined]) {
        const got = evaluateLicense({ status, expiresAt: "2099-01-16T00:00:00+07:00" });
        assert.equal(got.allowed, false, `status=${status}`);
        assert.notEqual(got.effectiveStatus, "active");
    }
});

test("evaluateLicense treats a record with no expiresAt as perpetual (v1 keys)", () => {
    const got = evaluateLicense({ status: "active" });
    assert.equal(got.effectiveStatus, "active");
    assert.equal(got.allowed, true);
    assert.equal(got.expiresAt, null);
});

test("evaluateLicense walks active -> grace -> expired around the anchor", () => {
    const record = { status: "active", expiresAt: "2026-09-16T00:00:00+07:00" };

    const active = evaluateLicense(record, vn("2026-09-15T23:59:00+07:00"));
    assert.equal(active.effectiveStatus, "active");
    assert.equal(active.allowed, true);
    assert.equal(active.daysRemaining, 1);

    const atAnchor = evaluateLicense(record, vn("2026-09-16T00:00:00+07:00"));
    assert.equal(atAnchor.effectiveStatus, "active", "the anchor instant itself is still valid");

    const grace = evaluateLicense(record, vn("2026-09-20T12:00:00+07:00"));
    assert.equal(grace.effectiveStatus, "grace");
    assert.equal(grace.allowed, true, "grace must keep the station running");
    assert.equal(grace.graceUntil, "2026-09-23T00:00:00+07:00");

    const expired = evaluateLicense(record, vn("2026-09-23T00:00:01+07:00"));
    assert.equal(expired.effectiveStatus, "expired");
    assert.equal(expired.allowed, false);
});

test("evaluateLicense honours a per-license graceDays override", () => {
    const record = { status: "active", expiresAt: "2026-09-16T00:00:00+07:00", graceDays: 0 };
    const got = evaluateLicense(record, vn("2026-09-16T00:00:01+07:00"));
    assert.equal(got.effectiveStatus, "expired");
    assert.equal(got.allowed, false);
});

test("evaluateLicense falls back to the default grace window on a bad graceDays", () => {
    for (const graceDays of [undefined, null, "", "abc", -3]) {
        const got = evaluateLicense(
            { status: "active", expiresAt: "2026-09-16T00:00:00+07:00", graceDays },
            vn("2026-09-16T00:00:00+07:00")
        );
        const expected = vn("2026-09-16T00:00:00+07:00") + DEFAULT_GRACE_DAYS * 86_400_000;
        assert.equal(got.graceUntilMs, expected, `graceDays=${JSON.stringify(graceDays)}`);
    }
});

test("evaluateLicense reads a legacy expiresAt string", () => {
    const got = evaluateLicense(
        { status: "active", expiresAt: "16-09-2026 00:00" },
        vn("2026-09-10T00:00:00+07:00")
    );
    assert.equal(got.effectiveStatus, "active");
    assert.equal(got.expiresAt, "2026-09-16T00:00:00+07:00");
});

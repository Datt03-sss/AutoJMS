"use strict";

// ==========================================================================
// backend/firebase/database.rules.json — the Realtime Database's own gate.
// ==========================================================================
// That database holds every customer's licence key and every live session. Until
// this file existed the rules were unversioned: whatever was last clicked in the
// Firebase console, with no record of it and nothing to notice a change.
//
// Two properties are pinned here, and neither is observable from the API tests.
// The Admin SDK bypasses rules entirely, so a wide-open database and a locked one
// respond identically to every request in this suite — the only way this can fail
// is deliberately, by reading the file.
//
//   1. Nothing is readable or writable by a client. The desktop app never talks to
//      RTDB (no databaseURL anywhere in src/AutoJMS), so the server is the only
//      legitimate reader and it is not subject to rules. "Deny everything" is
//      therefore the complete policy, not a restriction of one.
//
//   2. /sessions is indexed on the child the server actually queries by. Without
//      it Firebase downloads the whole sessions node on every login and filters in
//      process — correct, silent, and slower with every customer added.
//
// The file is strict JSON on purpose: it is what the owner pastes into the console
// and what `firebase deploy --only database` would read, and neither accepts
// comments.
// ==========================================================================

const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const RULES_PATH = path.join(__dirname, "..", "..", "firebase", "database.rules.json");
const RAW_RULES = fs.readFileSync(RULES_PATH, "utf8");

const SERVER_SOURCE = fs.readFileSync(path.join(__dirname, "..", "server.js"), "utf8");

test("the rules file is strict JSON with no byte-order mark", () => {
    // A BOM is invisible in an editor and breaks JSON.parse here, in the Firebase
    // console's paste box, and in the CLI's deploy step.
    assert.ok(!RAW_RULES.startsWith("\uFEFF"), "rules must not start with a BOM");
    assert.doesNotThrow(() => JSON.parse(RAW_RULES));

    const parsed = JSON.parse(RAW_RULES);
    assert.ok(parsed.rules && typeof parsed.rules === "object", "must have a top-level 'rules' object");
});

test("no client can read or write anything", () => {
    const { rules } = JSON.parse(RAW_RULES);

    // Booleans, not the strings "false". Firebase treats a string as an expression,
    // so ".read": "false" also denies — but it is one typo away from ".read": "true",
    // which reads as ordinary quoting rather than as granting the world access to
    // every licence key.
    assert.equal(rules[".read"], false);
    assert.equal(rules[".write"], false);

    // Rules cascade DOWNWARD: a child that grants access overrides the root's denial
    // for its whole subtree, and the root keeps looking correct. So the check has to
    // walk the tree rather than read the two lines above.
    const grants = [];
    const walk = (node, at) => {
        if (!node || typeof node !== "object") return;
        for (const [key, value] of Object.entries(node)) {
            if (key === ".read" || key === ".write") {
                if (value !== false) grants.push(`${at}/${key} = ${JSON.stringify(value)}`);
            } else if (!key.startsWith(".")) {
                walk(value, `${at}/${key}`);
            }
        }
    };
    walk(rules, "");

    assert.deepEqual(grants, [], "every .read/.write in the file must be false");
});

test("/sessions is indexed on the child the server queries by", () => {
    const { rules } = JSON.parse(RAW_RULES);

    assert.ok(rules.sessions, "the rules must declare a 'sessions' node");
    assert.equal(rules.sessions[".indexOn"], "licenseKey");

    // The coupling, not just the value: server.js:952 runs
    // ref("sessions").orderByChild("licenseKey") on every verify-license call, and an
    // index that names a different child is the same as no index at all. If a query
    // here changes — or a second one appears on another node — this fails and someone
    // has to decide what to index, instead of the slowdown appearing months later as
    // a login that got gradually worse.
    const queriedKeys = [...SERVER_SOURCE.matchAll(/\.orderByChild\(\s*"([^"]+)"\s*\)/g)].map(m => m[1]);

    assert.deepEqual(
        [...new Set(queriedKeys)],
        ["licenseKey"],
        "server.js orders by a child this rules file does not index"
    );
});

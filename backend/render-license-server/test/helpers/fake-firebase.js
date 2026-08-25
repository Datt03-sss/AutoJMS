"use strict";

// ==========================================================================
// An in-memory stand-in for the firebase-admin surface server.js uses.
// ==========================================================================
// Only the calls server.js actually makes are implemented, and each one is
// implemented to behave the way the real Realtime Database behaves in the cases
// the routes branch on — a missing node yields a snapshot whose exists() is false
// and whose val() is null, not a throw.
//
// It also records every path read and written. Several of the behaviours worth
// testing are absences: "a malformed license key must not reach ref()", "an
// expired license must not mint a Google token". Those are only observable if the
// double remembers what it was asked to do.
// ==========================================================================

/** Deep clone so a route mutating what it read cannot alter the stored copy. */
const clone = value => (value === undefined || value === null ? value : structuredClone(value));

function splitPath(rawPath) {
    return String(rawPath)
        .split("/")
        .map(segment => segment.trim())
        .filter(Boolean);
}

function makeSnapshot(key, value) {
    const resolved = value === undefined ? null : value;

    return {
        key,
        exists: () => resolved !== null,
        val: () => clone(resolved),
        forEach(callback) {
            if (resolved === null || typeof resolved !== "object") return false;
            for (const [childKey, childValue] of Object.entries(resolved)) {
                if (callback(makeSnapshot(childKey, childValue)) === true) return true;
            }
            return false;
        }
    };
}

/**
 * @param {object} [seed] initial database contents, e.g. { Licenses: {...}, sessions: {...} }
 */
function createFakeFirebase(seed = {}) {
    const state = {
        data: clone(seed) || {},
        reads: [],
        writes: [],
        certifiedServiceAccount: null,
        initializeAppOptions: null,
        /** Set to a message to make the next database operation reject. */
        failNextWith: null
    };

    const readAt = segments => {
        let node = state.data;
        for (const segment of segments) {
            if (node === null || typeof node !== "object") return null;
            if (!Object.prototype.hasOwnProperty.call(node, segment)) return null;
            node = node[segment];
        }
        return node === undefined ? null : node;
    };

    const parentOf = segments => {
        let node = state.data;
        for (const segment of segments.slice(0, -1)) {
            if (node[segment] === null || typeof node[segment] !== "object") node[segment] = {};
            node = node[segment];
        }
        return node;
    };

    const maybeFail = async () => {
        if (!state.failNextWith) return;
        const message = state.failNextWith;
        state.failNextWith = null;
        throw new Error(message);
    };

    function makeRef(rawPath) {
        const segments = splitPath(rawPath);
        const leaf = segments.length > 0 ? segments[segments.length - 1] : "";

        const once = async () => {
            await maybeFail();
            state.reads.push(rawPath);
            // .info/connected is a server-side pseudo node; the real database always
            // answers it, so it must never depend on the seed.
            if (rawPath === ".info/connected") return makeSnapshot("connected", true);
            return makeSnapshot(leaf, readAt(segments));
        };

        return {
            path: rawPath,

            once,

            async set(value) {
                await maybeFail();
                state.writes.push({ op: "set", path: rawPath, value: clone(value) });
                parentOf(segments)[leaf] = clone(value);
            },

            async update(patch) {
                await maybeFail();
                state.writes.push({ op: "update", path: rawPath, value: clone(patch) });
                const parent = parentOf(segments);
                const current = parent[leaf];

                // The real update() treats a null value as a delete of that child,
                // which is exactly how verify-license clears stale sessions.
                const base = current && typeof current === "object" ? current : {};
                for (const [key, value] of Object.entries(clone(patch) || {})) {
                    const childSegments = splitPath(key);
                    if (childSegments.length > 1) {
                        // sessions.update({ "a/b": v }) is a relative multi-path write.
                        const target = parentOf([...segments, ...childSegments]);
                        const childLeaf = childSegments[childSegments.length - 1];
                        if (value === null) delete target[childLeaf];
                        else target[childLeaf] = value;
                        continue;
                    }
                    if (value === null) delete base[key];
                    else base[key] = value;
                }
                parent[leaf] = base;
            },

            async remove() {
                await maybeFail();
                state.writes.push({ op: "remove", path: rawPath });
                delete parentOf(segments)[leaf];
            },

            limitToFirst(count) {
                return {
                    once: async () => {
                        await maybeFail();
                        state.reads.push(`${rawPath}#limitToFirst(${count})`);
                        const value = readAt(segments);
                        if (value === null || typeof value !== "object") {
                            return makeSnapshot(leaf, value);
                        }
                        return makeSnapshot(leaf, Object.fromEntries(Object.entries(value).slice(0, count)));
                    }
                };
            },

            orderByChild(childKey) {
                return {
                    equalTo(expected) {
                        return {
                            once: async () => {
                                await maybeFail();
                                state.reads.push(`${rawPath}#${childKey}=${expected}`);
                                const value = readAt(segments);
                                if (value === null || typeof value !== "object") {
                                    return makeSnapshot(leaf, null);
                                }
                                const matched = Object.fromEntries(
                                    Object.entries(value).filter(([, child]) => child?.[childKey] === expected)
                                );
                                return makeSnapshot(leaf, Object.keys(matched).length > 0 ? matched : null);
                            }
                        };
                    }
                };
            }
        };
    }

    const admin = {
        credential: {
            cert(serviceAccount) {
                state.certifiedServiceAccount = clone(serviceAccount);
                return { kind: "fake-certificate" };
            }
        },

        initializeApp(options) {
            state.initializeAppOptions = { databaseURL: options?.databaseURL };
            return { name: "[DEFAULT]" };
        },

        database() {
            return { ref: makeRef };
        }
    };

    return {
        admin,

        /** Replace the whole database and clear the recorded calls. */
        reset(nextSeed = {}) {
            state.data = clone(nextSeed) || {};
            state.reads.length = 0;
            state.writes.length = 0;
            state.failNextWith = null;
        },

        /** Direct access for arrange/assert; returns the live object, not a copy. */
        raw: () => state.data,

        read: rawPath => clone(readAt(splitPath(rawPath))),

        reads: () => [...state.reads],

        writes: () => [...state.writes],

        removed: rawPath => state.writes.some(write => write.op === "remove" && write.path === rawPath),

        certifiedServiceAccount: () => clone(state.certifiedServiceAccount),

        databaseUrl: () => state.initializeAppOptions?.databaseURL,

        failNextWith(message) {
            state.failNextWith = message;
        }
    };
}

module.exports = { createFakeFirebase };

"use strict";

// Render sends SIGTERM on every deploy, then SIGKILL about 30 seconds later. The
// requests this server serves are not read-only — verify-license binds hwid and
// activatedAt and writes the session row — so a process that dies mid-write leaves
// a client retrying against a half-written session.
//
// The sequence is process-level behaviour, so it is driven through the exported
// factory rather than by signalling the test runner (which would kill the run).
// The signal handlers themselves are registered only in server.js's entry-point
// branch; what is asserted here is exactly what those handlers call.

const test = require("node:test");
const assert = require("node:assert/strict");

const { startServer } = require("./helpers/harness");

let createShutdownHandler;
let harness;

/**
 * A stand-in for an http.Server. close() records the call and hands back its
 * callback so a test decides when — and whether — the close completes.
 */
function fakeServer({ withCloseIdle = true } = {}) {
    const state = { closeCalls: 0, idleCalls: 0, callback: null };

    const server = {
        close(callback) {
            state.closeCalls += 1;
            state.callback = callback;
        }
    };

    if (withCloseIdle) {
        server.closeIdleConnections = () => {
            state.idleCalls += 1;
        };
    }

    return { server, state };
}

/** Collects exit codes instead of ending the test run. */
function recorder() {
    const codes = [];
    return { codes, exit: code => codes.push(code) };
}

test.before(async () => {
    harness = await startServer({ seed: {} });
    createShutdownHandler = harness.app.createShutdownHandler;
});

test.after(async () => {
    await harness.close();
});

test("a clean close exits zero", async () => {
    const { server, state } = fakeServer();
    const { codes, exit } = recorder();

    createShutdownHandler({ server, timeoutMs: 5000, exit })("SIGTERM");

    assert.equal(state.closeCalls, 1);
    // Nothing has exited yet: close() is still waiting on in-flight requests,
    // which is the entire point of the handler.
    assert.deepEqual(codes, []);

    state.callback();

    assert.deepEqual(codes, [0]);
});

test("idle keep-alive sockets are closed so close() can finish", async () => {
    const { server, state } = fakeServer();
    const { exit } = recorder();

    createShutdownHandler({ server, timeoutMs: 5000, exit })("SIGTERM");

    // close() waits for every connection, and an idle keep-alive socket counts as
    // one. The desktop client uses keep-alive, so without this the callback can
    // wait out the whole keepAliveTimeout on a server with nothing left to serve.
    assert.equal(state.idleCalls, 1);
});

test("a server without closeIdleConnections still shuts down", async () => {
    const { server, state } = fakeServer({ withCloseIdle: false });
    const { codes, exit } = recorder();

    // Feature-detected rather than assumed: the method arrived in Node 18.2, and a
    // missing one must not turn a graceful shutdown into a TypeError.
    createShutdownHandler({ server, timeoutMs: 5000, exit })("SIGTERM");
    state.callback();

    assert.equal(state.closeCalls, 1);
    assert.deepEqual(codes, [0]);
});

test("a second signal is ignored rather than closing twice", async () => {
    const { server, state } = fakeServer();
    const { codes, exit } = recorder();
    const shutdown = createShutdownHandler({ server, timeoutMs: 5000, exit });

    shutdown("SIGTERM");
    shutdown("SIGINT");
    shutdown("SIGTERM");

    // Without the guard, close() on an already-closing server hands
    // ERR_SERVER_NOT_RUNNING to the callback — reporting a clean shutdown as a
    // failed one, and exiting 1 on a deploy that went fine.
    assert.equal(state.closeCalls, 1);

    state.callback();

    assert.deepEqual(codes, [0]);
});

test("a failed close exits non-zero", async () => {
    const { server, state } = fakeServer();
    const { codes, exit } = recorder();

    createShutdownHandler({ server, timeoutMs: 5000, exit })("SIGTERM");
    state.callback(new Error("ERR_SERVER_NOT_RUNNING"));

    // The exit code is what a platform reads to decide whether the instance went
    // down cleanly, so a close that failed must not report success.
    assert.deepEqual(codes, [1]);
});

test("a close that never completes is forced out before SIGKILL", async () => {
    const { server, state } = fakeServer();
    const { codes, exit } = recorder();

    createShutdownHandler({ server, timeoutMs: 20, exit })("SIGTERM");

    // The forced timer is unref'd on purpose, so it is not allowed to be the only
    // thing keeping the loop alive. In production an in-flight request holds its
    // own socket handle; here a ref'd guard stands in for that, otherwise the test
    // process could exit before the deadline it is trying to observe.
    const guard = setTimeout(() => {}, 5000);

    try {
        await new Promise(resolve => setTimeout(resolve, 120));
    } finally {
        clearTimeout(guard);
    }

    // A request wedged on a Firebase call that never settles must not hold the
    // process past Render's SIGKILL, which would kill it with no log line at all.
    assert.deepEqual(codes, [1]);
    assert.equal(state.closeCalls, 1);
});

test("a completed shutdown does not exit again when the deadline passes", async () => {
    const { server, state } = fakeServer();
    const { codes, exit } = recorder();

    createShutdownHandler({ server, timeoutMs: 20, exit })("SIGTERM");
    state.callback();

    const guard = setTimeout(() => {}, 5000);

    try {
        await new Promise(resolve => setTimeout(resolve, 120));
    } finally {
        clearTimeout(guard);
    }

    // clearTimeout on the success path: a second exit(1) arriving after a clean
    // exit(0) would make every graceful shutdown look like a forced one in the log.
    assert.deepEqual(codes, [0]);
});

test("a zero timeout is clamped instead of meaning immediate", async () => {
    // SHUTDOWN_TIMEOUT_MS=0 read literally would force-exit before close() could
    // drain anything — the one value that turns this safeguard into its opposite.
    const bare = await startServer({ seed: {}, env: { SHUTDOWN_TIMEOUT_MS: "0" } });

    try {
        assert.equal(bare.app.shutdownTimeoutMs, 1000);
    } finally {
        await bare.close();
    }
});

test("an operator-supplied timeout is honoured", async () => {
    const bare = await startServer({ seed: {}, env: { SHUTDOWN_TIMEOUT_MS: "20000" } });

    try {
        assert.equal(bare.app.shutdownTimeoutMs, 20000);
    } finally {
        await bare.close();
    }
});

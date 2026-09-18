"use strict";

// ==========================================================================
// Fleet-wide update directive + per-licence app version telemetry.
// ==========================================================================
// Two features that share one Firebase node each and one shared failure mode:
// both are things the client is told, so both run on the hot paths the whole
// fleet hits once a minute. The tests are weighted towards what happens when
// they are ABSENT or BROKEN — a broadcast that cannot be read must not take an
// activation down with it, and a version nobody reported must not blank the one
// already stored.
//
// The three cache/interval knobs are pinned per boot rather than left at their
// defaults: `broadcastCache` and the activity interval live in server.js's
// module body, so within one file they survive db.reset() and a test that
// forgot them would read the previous test's directive.
// ==========================================================================

const test = require("node:test");
const assert = require("node:assert/strict");
const http = require("node:http");

const { FIXTURE, startServer, activeLicense, activeSession, seedWithActiveSession } = require("./helpers/harness");

process.setMaxListeners(64);

const ADMIN_TOKEN = "test-admin-token-0123456789";
const withAuth = (options = {}) => ({
    ...options,
    headers: { "x-admin-token": ADMIN_TOKEN, ...(options.headers || {}) }
});

/** A stored directive, in the shape POST /broadcast-update writes. */
const storedDirective = (overrides = {}) => ({
    active: true,
    version: "1.26.12",
    channel: "stable",
    message: "Bản vá lỗi in đơn.",
    updatedAt: Date.now(),
    ...overrides
});

// ==========================================================================
// THE CLIENT-FACING HALF — verify-license and heartbeat
// ==========================================================================
//
// Both knobs are zeroed here so each request re-reads Firebase and each
// heartbeat writes. The rationing and the caching each get their own boot
// below, where the clock is the thing under test rather than noise.

test.describe("verify-license and heartbeat", () => {
    let harness;

    test.before(async () => {
        harness = await startServer({
            env: { BROADCAST_UPDATE_CACHE_MS: 0, LICENSE_ACTIVITY_WRITE_INTERVAL_MS: 0 }
        });
    });

    test.after(async () => {
        await harness.close();
    });

    const seed = (extra = {}) =>
        harness.db.reset({
            Licenses: { [FIXTURE.licenseKey]: activeLicense() },
            sessions: { [FIXTURE.sessionId]: activeSession() },
            ...extra
        });

    const verify = (body = {}) =>
        harness.post("/api/verify-license", {
            body: { licenseKey: FIXTURE.licenseKey, hwid: FIXTURE.hwid, ...body }
        });

    const beat = (body = {}) => harness.post("/api/heartbeat", { body: { ...body } , token: harness.signToken() });

    test("activation records the build the station reports", async () => {
        seed();

        const response = await verify({ appVersion: "1.26.12" });
        assert.equal(response.status, 200);

        const record = harness.db.read(`Licenses/${FIXTURE.licenseKey}`);
        assert.equal(record.appVersion, "1.26.12");
        assert.ok(record.lastActiveAt > 0, "lastActiveAt is what the dashboard shows as 'last seen'");
    });

    test("a prerelease version survives intact", async () => {
        seed();

        await verify({ appVersion: "1.26.12-beta.1" });

        assert.equal(harness.db.read(`Licenses/${FIXTURE.licenseKey}`).appVersion, "1.26.12-beta.1");
    });

    test("a version that is not a version is dropped, and the stored one is kept", async () => {
        // The value arrives off the wire and is rendered in the admin table and
        // compared against on the station. Anything that is not a plain version
        // token must not reach either.
        harness.db.reset({
            Licenses: { [FIXTURE.licenseKey]: activeLicense({ appVersion: "1.26.11" }) }
        });

        await verify({ appVersion: "<img src=x onerror=alert(1)>" });

        const record = harness.db.read(`Licenses/${FIXTURE.licenseKey}`);
        assert.equal(record.appVersion, "1.26.11", "a rejected report must not blank the last known build");
        assert.ok(record.lastActiveAt > 0, "the station was still seen, whatever it claimed to be running");
    });

    test("a client too old to report a version still activates", async () => {
        seed();

        const response = await verify();

        assert.equal(response.status, 200);
        assert.ok(response.body.payload, "telemetry must never be a reason activation fails");
        assert.equal(harness.db.read(`Licenses/${FIXTURE.licenseKey}`).appVersion, undefined);
    });

    test("no broadcast means no broadcastUpdate key at all", async () => {
        seed();

        const response = await verify({ appVersion: "1.26.12" });

        assert.equal(response.status, 200);
        assert.equal(
            "broadcastUpdate" in response.body,
            false,
            "an empty object here is a directive the station has to reason about"
        );
    });

    test("an active broadcast reaches verify-license", async () => {
        seed({ config: { broadcastUpdate: storedDirective() } });

        const response = await verify({ appVersion: "1.26.11" });

        assert.deepEqual(response.body.broadcastUpdate, {
            version: "1.26.12",
            channel: "stable",
            message: "Bản vá lỗi in đơn.",
            updatedAt: harness.db.read("config/broadcastUpdate").updatedAt
        });
    });

    test("an active broadcast reaches the heartbeat in the same shape", async () => {
        seed({ config: { broadcastUpdate: storedDirective({ channel: "beta", version: "1.27.0-beta.2" }) } });

        const response = await beat({ appVersion: "1.26.12" });

        assert.equal(response.status, 200);
        assert.equal(response.body.action, "continue");
        assert.equal(response.body.broadcastUpdate.version, "1.27.0-beta.2");
        assert.equal(response.body.broadcastUpdate.channel, "beta");
    });

    test("a switched-off broadcast is not sent", async () => {
        seed({ config: { broadcastUpdate: storedDirective({ active: false }) } });

        const response = await verify({ appVersion: "1.26.11" });

        assert.equal("broadcastUpdate" in response.body, false);
    });

    test("a broadcast with an unusable version is treated as no broadcast", async () => {
        // Otherwise every station in the fleet compares against garbage and either
        // all update or none do, decided by a typo in a form field.
        seed({ config: { broadcastUpdate: storedDirective({ version: "  " }) } });

        const response = await verify({ appVersion: "1.26.11" });

        assert.equal("broadcastUpdate" in response.body, false);
    });

    test("an unknown channel falls back to stable rather than being forwarded", async () => {
        seed({ config: { broadcastUpdate: storedDirective({ channel: "nightly" }) } });

        const response = await verify({ appVersion: "1.26.11" });

        assert.equal(response.body.broadcastUpdate.channel, "stable");
    });

    test("an over-long message is truncated to what the client will show", async () => {
        seed({ config: { broadcastUpdate: storedDirective({ message: "x".repeat(1000) }) } });

        const response = await verify({ appVersion: "1.26.11" });

        assert.equal(response.body.broadcastUpdate.message.length, 300);
    });

    test("the heartbeat records a version change made while the station was running", async () => {
        harness.db.reset(seedWithActiveSession({ appVersion: "1.26.11" }));

        const response = await beat({ appVersion: "1.26.12" });

        assert.equal(response.status, 200);
        assert.equal(harness.db.read(`Licenses/${FIXTURE.licenseKey}`).appVersion, "1.26.12");
    });
});

// ==========================================================================
// THE RATIONING
// ==========================================================================

test("the heartbeat does not rewrite the licence record on every beat", async () => {
    // The heartbeat runs once a minute per station. Writing the licence record
    // each time would make a presence indicator the busiest write in the system
    // while carrying no more information than a ten-minute-old one.
    const harness = await startServer({
        env: { BROADCAST_UPDATE_CACHE_MS: 0, LICENSE_ACTIVITY_WRITE_INTERVAL_MS: 600_000 }
    });

    try {
        harness.db.reset(seedWithActiveSession({ appVersion: "1.26.12", lastActiveAt: Date.now() }));

        const response = await harness.post("/api/heartbeat", {
            body: { appVersion: "1.26.12" },
            token: harness.signToken()
        });

        assert.equal(response.status, 200);
        assert.equal(
            harness.db.writes().some(write => write.path === `Licenses/${FIXTURE.licenseKey}`),
            false,
            "same version, seen seconds ago: there is nothing new to record"
        );
    } finally {
        await harness.close();
    }
});

test("a version change is written immediately, whatever the interval says", async () => {
    const harness = await startServer({
        env: { BROADCAST_UPDATE_CACHE_MS: 0, LICENSE_ACTIVITY_WRITE_INTERVAL_MS: 600_000 }
    });

    try {
        harness.db.reset(seedWithActiveSession({ appVersion: "1.26.11", lastActiveAt: Date.now() }));

        await harness.post("/api/heartbeat", {
            body: { appVersion: "1.26.12" },
            token: harness.signToken()
        });

        assert.equal(harness.db.read(`Licenses/${FIXTURE.licenseKey}`).appVersion, "1.26.12");
    } finally {
        await harness.close();
    }
});

// ==========================================================================
// THE CACHE
// ==========================================================================

test("the broadcast node is read once per TTL, not once per request", async () => {
    const harness = await startServer({ env: { BROADCAST_UPDATE_CACHE_MS: 60_000 } });

    try {
        harness.db.reset({
            Licenses: { [FIXTURE.licenseKey]: activeLicense() },
            config: { broadcastUpdate: storedDirective({ version: "1.26.12" }) }
        });

        const verify = () =>
            harness.post("/api/verify-license", {
                body: { licenseKey: FIXTURE.licenseKey, hwid: FIXTURE.hwid, appVersion: "1.26.11" }
            });

        const first = await verify();
        assert.equal(first.body.broadcastUpdate.version, "1.26.12");

        // Changed underneath the cache. A second read would see it; the cache
        // must not, or the TTL is doing nothing and every station in the fleet
        // is paying a Firebase round-trip a minute for a node that changes when
        // the owner presses a button.
        harness.db.raw().config.broadcastUpdate.version = "9.99.99";

        const second = await verify();
        assert.equal(second.body.broadcastUpdate.version, "1.26.12");

        assert.equal(
            harness.db.reads().filter(path => path === "config/broadcastUpdate").length,
            1,
            "two activations, one read"
        );
    } finally {
        await harness.close();
    }
});

test("a broadcast node that is not a directive does not take the activation down with it", async () => {
    const harness = await startServer({ env: { BROADCAST_UPDATE_CACHE_MS: 0 } });

    try {
        // A hand-edited Firebase console, or a half-finished migration. The node
        // is unusable either way, and a station must start regardless — the
        // broadcast is the least important thing this route returns.
        harness.db.reset({
            Licenses: { [FIXTURE.licenseKey]: activeLicense() },
            config: { broadcastUpdate: "cập nhật ngay" }
        });

        const response = await harness.post("/api/verify-license", {
            body: { licenseKey: FIXTURE.licenseKey, hwid: FIXTURE.hwid, appVersion: "1.26.11" }
        });

        assert.equal(response.status, 200);
        assert.ok(response.body.payload, "a broadcast is not worth refusing to start the app over");
        assert.equal("broadcastUpdate" in response.body, false);
    } finally {
        await harness.close();
    }
});

// ==========================================================================
// THE ADMIN HALF
// ==========================================================================

test.describe("admin broadcast routes", () => {
    let harness;

    test.before(async () => {
        harness = await startServer({ env: { ADMIN_SECRET_TOKEN: ADMIN_TOKEN, BROADCAST_UPDATE_CACHE_MS: 0 } });
    });

    test.after(async () => {
        await harness.close();
    });

    test("with no directive stored the GET answers with an off switch, not a 404", async () => {
        harness.db.reset({});

        const response = await harness.get("/api/admin/broadcast-update", withAuth());

        assert.equal(response.status, 200);
        assert.deepEqual(response.body.broadcastUpdate, {
            active: false,
            version: "",
            channel: "stable",
            message: "",
            updatedAt: 0
        });
    });

    test("switching a broadcast on stores exactly what the client will read", async () => {
        harness.db.reset({});

        const response = await harness.post(
            "/api/admin/broadcast-update",
            withAuth({ body: { active: true, version: "1.26.12", channel: "beta", message: "Cập nhật trước ca chiều." } })
        );

        assert.equal(response.status, 200);
        assert.equal(response.body.active, true);

        const stored = harness.db.read("config/broadcastUpdate");
        assert.equal(stored.active, true);
        assert.equal(stored.version, "1.26.12");
        assert.equal(stored.channel, "beta");
        assert.equal(stored.message, "Cập nhật trước ca chiều.");
        assert.ok(stored.updatedAt > 0);
    });

    test("switching it off keeps the version and message it carried", async () => {
        // The modal reopens on the last broadcast rather than a blank form, so
        // re-sending the same one is one click.
        harness.db.reset({ config: { broadcastUpdate: storedDirective() } });

        const response = await harness.post("/api/admin/broadcast-update", withAuth({ body: { active: false } }));

        assert.equal(response.status, 200);
        assert.equal(response.body.active, false);

        const stored = harness.db.read("config/broadcastUpdate");
        assert.equal(stored.active, false);
        assert.equal(stored.version, "1.26.12");
        assert.equal(stored.message, "Bản vá lỗi in đơn.");
    });

    test("a version the client would reject is refused here, and nothing is written", async () => {
        harness.db.reset({});

        const response = await harness.post(
            "/api/admin/broadcast-update",
            withAuth({ body: { active: true, version: "1.26.12 <script>", channel: "stable" } })
        );

        assert.equal(response.status, 400);
        assert.equal(response.body.error, "INVALID_BROADCAST_VERSION");
        assert.deepEqual(harness.db.writes(), [], "a refused broadcast must not half-write");
    });

    test("an unknown channel is refused rather than silently corrected", async () => {
        harness.db.reset({});

        const response = await harness.post(
            "/api/admin/broadcast-update",
            withAuth({ body: { active: true, version: "1.26.12", channel: "nightly" } })
        );

        assert.equal(response.status, 400);
        assert.equal(response.body.error, "INVALID_UPDATE_CHANNEL");
        assert.deepEqual(harness.db.writes(), []);
    });

    test("the message is stripped of control characters and capped", async () => {
        harness.db.reset({});

        await harness.post(
            "/api/admin/broadcast-update",
            withAuth({ body: { active: true, version: "1.26.12", channel: "stable", message: "y".repeat(900) } })
        );

        assert.equal(harness.db.read("config/broadcastUpdate").message.length, 300);
    });

    test("both routes are behind the admin token like every other admin route", async () => {
        harness.db.reset({});

        const read = await harness.get("/api/admin/broadcast-update");
        const written = await harness.post("/api/admin/broadcast-update", { body: { active: false } });

        assert.equal(read.status, 401);
        assert.equal(written.status, 401);
        assert.deepEqual(harness.db.writes(), []);
    });

    test("the licence list carries the reported version for the whole fleet at once", async () => {
        // The "Phiên bản" column is read across every row, which is the entire
        // point of it — so it cannot wait for the per-row detail call.
        harness.db.reset({
            Licenses: {
                [FIXTURE.licenseKey]: activeLicense({ appVersion: "1.26.12", lastActiveAt: Date.now() })
            }
        });

        const response = await harness.get("/api/admin/licenses", withAuth());

        assert.equal(response.status, 200);
        assert.equal(response.body.licenses[0].appVersion, "1.26.12");
        assert.match(response.body.licenses[0].lastActiveAt, /\+07:00$/);
    });

    test("a licence nobody has reported for reads as empty, not as a zero date", async () => {
        harness.db.reset({ Licenses: { [FIXTURE.licenseKey]: activeLicense() } });

        const response = await harness.get(`/api/admin/licenses/${FIXTURE.licenseKey}`, withAuth());

        assert.equal(response.status, 200);
        assert.equal(response.body.license.appVersion, "");
        assert.equal(response.body.license.lastActiveAt, null);
    });

    test("a -beta version forces the beta channel even when stable was asked for", async () => {
        const harness = await startServer({ env: { ADMIN_SECRET_TOKEN: ADMIN_TOKEN } });
        try {
            harness.db.reset({});
            const res = await harness.post("/api/admin/broadcast-update", {
                body: { active: true, version: "1.26.12-beta.1", channel: "stable" },
                ...withAuth()
            });

            assert.equal(res.status, 200);
            // A beta build exists only on the beta feed. Honouring "stable" here
            // would send every station to a feed with no such release, and Velopack
            // would answer "you are up to date" — a broadcast that looks delivered
            // and changes nothing.
            assert.equal(harness.db.read("config/broadcastUpdate").channel, "beta");
        } finally { await harness.close(); }
    });

    test("a plain version keeps the channel the owner chose", async () => {
        const harness = await startServer({ env: { ADMIN_SECRET_TOKEN: ADMIN_TOKEN } });
        try {
            harness.db.reset({});
            const res = await harness.post("/api/admin/broadcast-update", {
                body: { active: true, version: "1.26.12", channel: "stable" },
                ...withAuth()
            });

            assert.equal(res.status, 200);
            assert.equal(harness.db.read("config/broadcastUpdate").channel, "stable");
        } finally { await harness.close(); }
    });
});

// ==========================================================================
// THE RELEASE LIST
// ==========================================================================
//
// Pointed at a local server rather than GitHub: a suite that needs the network
// is a suite that fails for reasons unrelated to the change being tested.

/** A one-route HTTP server; returns its origin and a close(). */
async function stubServer(handler) {
    const server = http.createServer(handler);
    await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));

    return {
        origin: `http://127.0.0.1:${server.address().port}`,
        close: () => new Promise(resolve => server.close(resolve))
    };
}

const GITHUB_PAYLOAD = [
    { tag_name: "v1.26.12", name: "AutoJMS 1.26.12", prerelease: false, draft: false, published_at: "2026-09-12T10:00:00Z" },
    { tag_name: "v1.26.12-beta.1", name: "AutoJMS 1.26.12 beta 1", prerelease: true, draft: false, published_at: "2026-09-11T10:00:00Z" },
    { tag_name: "v1.27.0", name: "Chưa phát hành", prerelease: false, draft: true, published_at: null }
];

test("the release list comes from GitHub, without the drafts", async () => {
    const github = await stubServer((req, res) => {
        res.writeHead(200, { "content-type": "application/json" });
        res.end(JSON.stringify(GITHUB_PAYLOAD));
    });

    const harness = await startServer({
        env: { ADMIN_SECRET_TOKEN: ADMIN_TOKEN, GITHUB_RELEASES_API_URL: `${github.origin}/releases` }
    });

    try {
        const response = await harness.get("/api/admin/releases", withAuth());

        assert.equal(response.status, 200);
        assert.equal(response.body.source, "github");
        assert.equal(response.body.releases.length, 2, "a draft is not downloadable, so it must not be offerable");

        // The leading v is what the tag carries and what AppVersion.Current does
        // not, and the two have to meet somewhere or every release looks newer
        // than the running build.
        assert.equal(response.body.releases[0].version, "1.26.12");
        assert.equal(response.body.releases[0].channel, "stable");
        assert.equal(response.body.releases[1].version, "1.26.12-beta.1");
        assert.equal(response.body.releases[1].channel, "beta");
    } finally {
        await harness.close();
        await github.close();
    }
});

test("a rate-limited GitHub falls back to the manifest the client itself reads", async () => {
    const github = await stubServer((req, res) => {
        res.writeHead(403, { "content-type": "application/json" });
        res.end(JSON.stringify({ message: "API rate limit exceeded" }));
    });

    const manifest = await stubServer((req, res) => {
        res.writeHead(200, { "content-type": "application/xml" });
        res.end(`<?xml version="1.0" encoding="utf-8"?>
<AutoJMSUpdate>
  <channel name="stable" enabled="true">
    <velopackVersion>1.26.12</velopackVersion>
    <releaseTag>v1.26.12</releaseTag>
  </channel>
  <channel name="beta" enabled="true" prerelease="true">
    <velopackVersion>1.26.12-beta.1</velopackVersion>
    <releaseTag>v1.26.12-beta.1</releaseTag>
  </channel>
</AutoJMSUpdate>`);
    });

    const harness = await startServer({
        env: {
            ADMIN_SECRET_TOKEN: ADMIN_TOKEN,
            GITHUB_RELEASES_API_URL: `${github.origin}/releases`,
            UPDATE_XML_URL: `${manifest.origin}/update.xml`
        }
    });

    try {
        const response = await harness.get("/api/admin/releases", withAuth());

        assert.equal(response.status, 200);
        assert.equal(response.body.source, "update.xml");
        assert.deepEqual(
            response.body.releases.map(release => [release.version, release.channel]),
            [
                ["1.26.12", "stable"],
                ["1.26.12-beta.1", "beta"]
            ]
        );
    } finally {
        await harness.close();
        await github.close();
        await manifest.close();
    }
});

test("both sources down is a 502, not an empty dropdown", async () => {
    // An empty list looks exactly like "no releases have been published", and the
    // owner would go looking at the wrong repository.
    const down = await stubServer((req, res) => {
        res.writeHead(500);
        res.end("nope");
    });

    const harness = await startServer({
        env: {
            ADMIN_SECRET_TOKEN: ADMIN_TOKEN,
            GITHUB_RELEASES_API_URL: `${down.origin}/releases`,
            UPDATE_XML_URL: `${down.origin}/update.xml`
        }
    });

    try {
        const response = await harness.get("/api/admin/releases", withAuth());

        assert.equal(response.status, 502);
        assert.equal(response.body.error, "RELEASES_UNAVAILABLE");
    } finally {
        await harness.close();
        await down.close();
    }
});

test("the release list is behind the admin token too", async () => {
    const harness = await startServer({ env: { ADMIN_SECRET_TOKEN: ADMIN_TOKEN } });

    try {
        const response = await harness.get("/api/admin/releases");
        assert.equal(response.status, 401);
    } finally {
        await harness.close();
    }
});

test("a release tag keeps its -Release suffix out of the version", async () => {
    const github = await stubServer((req, res) => {
        res.writeHead(200, { "content-type": "application/json" });
        res.end(JSON.stringify([
            { tag_name: "v1.26.12-Release", name: "AutoJMS 1.26.12", prerelease: false, draft: false, published_at: "2026-09-01T00:00:00Z" },
            { tag_name: "v1.26.13-beta.1-Release", name: "AutoJMS 1.26.13-beta.1", prerelease: true, draft: false, published_at: "2026-09-02T00:00:00Z" }
        ]));
    });

    const harness = await startServer({
        env: { ADMIN_SECRET_TOKEN: ADMIN_TOKEN, GITHUB_RELEASES_API_URL: `${github.origin}/releases` }
    });

    try {
        const res = await harness.get("/api/admin/releases", withAuth());
        assert.equal(res.status, 200);

        const versions = res.body.releases.map(r => r.version);
        // AutoJMS tags every build `-Release`. Left on, the client's semver
        // comparison reads it as a prerelease label and refuses the upgrade.
        assert.ok(versions.includes("1.26.12"), `stable version not normalised: ${versions.join(", ")}`);
        assert.ok(versions.includes("1.26.13-beta.1"), `beta version not normalised: ${versions.join(", ")}`);
    } finally {
        await harness.close();
        await github.close();
    }
});

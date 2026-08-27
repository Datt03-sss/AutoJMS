# AutoJMS License Server

This service verifies licenses and issues the signed desktop assertion. It does
not connect to PostgreSQL and it never receives or returns a DataHub device
token. The desktop enrolls its device with the VPS DataHub API separately.

> **Deploy source warning.** Render production currently serves
> `Datt03-sss/AutoJMS-API`, not this directory — proven by `GET
> /health/firebase/licenses` returning 200 in production while that route has
> never existed in this repo. Nothing in this directory affects production until
> the two copies are reconciled. See section L of
> `docs/agent/FULLSTACK_BACKEND_RISK_REVIEW.md`.

## Local setup

Run npm ci, copy env.template to .env, then run npm run check and npm start.

Required secrets:

- JWT_PRIVATE_KEY
- JWT_PUBLIC_KEY
- Firebase Admin credentials through one supported source

The only DataHub setting is the public API base URL:

DATAHUB_API_BASE_URL=https://datahub.example.com

The VPS API owns database credentials, device enrollment, leases, ingest and
SignalR. Do not place PostgreSQL passwords, service keys, or device tokens in
this service or in a desktop license response.

## Firebase credentials

`firebase-credentials.js` owns credential resolution, and it is the only place
that reads these variables. Sources are tried in this order and the first one
that is **set** must be usable — a present-but-malformed source throws instead
of falling through, so a typo cannot be reported as "no credentials
configured":

1. `FIREBASE_SERVICE_ACCOUNT_JSON` — inline JSON
2. `FIREBASE_SERVICE_ACCOUNT_BASE64` — base64 of the same JSON
3. `FIREBASE_SERVICE_ACCOUNT_FILE` — explicit path
4. `GOOGLE_APPLICATION_CREDENTIALS` — conventional ADC path
5. `./serviceAccountKey.json` next to server.js — last resort

Source 3 is the one to use with a Render Secret File. A Secret File is mounted
under whatever name it was given and frequently has **no `.json` extension**,
which is why the file is read and `JSON.parse`d rather than `require()`d; a
regression back to `require()` fails on exactly the deployment shape production
uses. `GOOGLE_SHEETS_SERVICE_ACCOUNT_FILE` falls back to
`FIREBASE_SERVICE_ACCOUNT_FILE`, so a deployment that sets source 3 needs no
other credential variable.

If no source is set, the process logs the five names it looked for and exits on
boot. It does not start in a degraded state.

## CORS

`cors({ origin: false })`. Every real caller is a WinForms desktop using
`HttpClient`, which performs no CORS check at all, so the previous wildcard
bought nothing while telling any browser it could read these responses
cross-origin. Do not widen it to serve a browser tool; give the tool its own
service.

## Routes

| Route | Auth | Rate limit |
|---|---|---|
| `GET /health` | none | global only |
| `GET /health/firebase` | none | 30/min/IP |
| `GET /health/firebase/licenses` | none | 30/min/IP |
| `POST /api/verify-license` | none (license key + hwid) | 60/min/IP |
| `POST /api/heartbeat` | access token | 120/min/IP |
| `POST /api/google-sheets/grant` | access token | 60/min/IP |
| `POST /api/datahub/license-assertion` | access token | 60/min/IP |
| `POST /api/logout` | access token | 60/min/IP |

Every number above is **per IP**, and a NAT'd office shares one egress address:
ten stations behind one router are one caller to this limiter. verify-license is
60 rather than 20 for that reason — a launch is `verify-license` plus a Sheets
grant plus an assertion, so a morning where a whole office opens the app at once
was landing near a limit set for a single machine.

### The global flood guard

Above every route sits one limiter with no exemptions, registered **after**
`cors()` and **before** `express.json()`. That order is the whole point: a
per-route limiter is registered on its route, which puts it after the body
parser, so an unauthenticated caller could make this process allocate and parse
a 512 kB body on every request and only then be told it was over its limit. On
Render's free tier — 512 MB of RAM, one instance, no autoscale — the memory was
already spent by the time the check ran. `rate-limit.test.js` proves the
ordering by sending an unparseable body: under the limit it answers 400 (the
parser ran), over the limit 429 (it never reached the parser).

| Variable | Default | Floor |
|---|---|---|
| `GLOBAL_RATE_LIMIT_PER_MINUTE` | 600 | 120 |

600 is above the sum of every per-route cap (60 + 120 + 30 + 60 + 60 = 330), so
this can never become the binding limit for legitimate traffic — if it starts
refusing, the caller is not a station. The floor is 120 because that is
`heartbeatLimiter`'s own cap: below it, the global limiter would start refusing
heartbeats a route policy allows, and a global 429 also answers `/health`, which
Render polls — an instance answering 429 there is marked unhealthy and
restarted, i.e. a flood guard that takes down the service it defends. An
unparseable value falls back to 600, **not** to the floor: a typo in a Render
variable was never an instruction to tighten anything.

`GET /health` touches no database: Render polls it every few seconds, and a
read here would multiply Firebase cost by the platform's own health interval.

`GET /health/firebase` proves only that a socket exists. `GET
/health/firebase/licenses` proves the service account can still **read**
`/Licenses` — the failure a rules change causes, and the one a socket probe
reports as healthy. It reads `limitToFirst(1)`, so a poll costs one small node,
and it returns exactly `{ ok, service, readable, hasAny, elapsedMs }`: an
anonymous caller learns nothing about the fleet, not a key, not a count, not a
tier. `readable: true, hasAny: false` is worth distinguishing from a failure —
on a live deployment it means the wrong `FIREBASE_DATABASE_URL`, and every
verify-license is returning `LICENSE_NOT_FOUND` while the server looks fine.

Probe failures answer with a code (`FIREBASE_UNAVAILABLE`, `FIREBASE_TIMEOUT`)
and nothing else. A Firebase error string carries the database URL, the project
id, and sometimes the service account email; an operator reads it in the Render
log instead.

## Verification

A successful `POST /api/verify-license` response contains the signed license
payload and the DataHub `apiBaseUrl` and `siteCode`. The desktop uses
`apiBaseUrl` to reach the VPS, then completes device enrollment using the signed
assertion.

Production enrollment must use the asymmetric issuer validator configured by the
DataHub API.

Input guards run before any Firebase read, so a malformed request costs nothing:

| HTTP | error | When |
|---|---|---|
| 400 | MISSING_REQUIRED_FIELDS | `licenseKey` or `hwid` absent or empty |
| 400 | LICENSE_KEY_INVALID | key is not a string, is under 4 or over 128 chars, or contains `. # $ [ ] /` or a control byte |
| 400 | HWID_INVALID | hwid is under 8 or over 256 chars, or contains a control byte |

The key guard is a **path** guard, not a format check: `.#$[]/` are the
characters that would let a key traverse the Realtime Database path. Ordinary
shapes — spaces, Vietnamese letters, a bare `abcd` — stay valid, and the test
suite asserts that so the guard cannot quietly become a format whitelist that
locks out live keys.

## License lifecycle

A license is sold as a one-month term but always expires at 00:00
Asia/Ho_Chi_Minh on the 16th, so the fleet renews on one calendar date:

    expiresAt = earliest "day 16, 00:00 +07:00" that is >= (start day + 30 days)

`license-expiry.js` owns that rule and nothing else. Generate a value with:

    node -e "console.log(require('./license-expiry').computeExpiry('2026-08-24').expiresAt)"

A record with no `expiresAt` is treated as perpetual, which is what keeps the
existing fleet working. Past `expiresAt` the license enters `grace` for
LICENSE_GRACE_DAYS days — it still verifies, and the server logs
LICENSE_GRACE — then becomes `expired`.

### The gate runs on every credential-issuing route

`evaluateLicenseRecord` is shared, and all four routes that hand out something
usable call it. Verify-license alone is not enough: it runs at launch and
nowhere else, so a station left running would otherwise keep minting fresh
tokens, Sheets grants and DataHub assertions for a license that expired days
earlier — revoking a license only worked if the customer happened to restart
the app.

| Route | Expired license | Side effect |
|---|---|---|
| `verify-license` | 403 `LICENSE_EXPIRED` | no session is created |
| `heartbeat` | 401 `action: "kill"`, `LICENSE_EXPIRED` | the session row is removed |
| `google-sheets/grant` | 403 `LICENSE_EXPIRED` | no Google token is minted |
| `datahub/license-assertion` | 403 `LICENSE_EXPIRED` | no assertion is signed |

The heartbeat **removes the session**, not just the token. The session is what
the Sheets broker and the assertion route authenticate against, so leaving it
active would keep both of those working for a dead license. It also removes the
session when the license row is gone (`LICENSE_NOT_FOUND`) or has been re-bound
to another machine (`HWID_MISMATCH`).

A heartbeat in grace answers 200 and forwards `effectiveStatus`, `expiresAt`,
`graceUntil` and `daysRemaining` (negative inside grace). Nothing on the client
reads them yet — `LicenseApiService.cs` is a protected file — but the data has
to exist before an expiry warning can be built.

Tier still comes from the **token**, not from the license record the heartbeat
just read: a tier change takes effect on restart by owner decision
(2026-08-24), and the client caches its entitlement at launch. Reading it here
would make the heartbeat disagree with the running app rather than change it.

Other verify-license rejections added by this schema:

| HTTP | error | When |
|---|---|---|
| 403 | LICENSE_EXPIRED | past `expiresAt` + `graceDays` |
| 403 | LICENSE_TIER_INVALID | `tier` is not BASE or ULTRA |
| 403 | LICENSE_SITE_CODE_INVALID | `middleCode` is a placeholder, and REQUIRE_UNIQUE_SITE_CODE=1 |

`middleCode` is the DataHub site code. Every existing key still carries the
`"0000"` placeholder, so REQUIRE_UNIQUE_SITE_CODE defaults to 0 and the server
only logs LICENSE_SITE_CODE_PLACEHOLDER. Backfill first, then turn it on.

## DataHub assertion

`POST /api/datahub/license-assertion` re-signs the enrollment assertion for a
station that is already running. The wire format is
`v1rs256.<base64url payload>.<base64url signature>` with case-sensitive
PascalCase keys, parsed by `RsaLicenseAssertionValidator` on the VPS; a rename
in either half is a 401 with no explanation.

Deliberate asymmetry with the heartbeat: this route does **not** burn the
token's `jti`. The heartbeat owns replay detection, and burning it here would
kill the very session that is asking to stay connected. The test suite asserts
the same access token works twice, so nobody "fixes" it into a replay check by
symmetry.

A license with no site code gets 503 `ASSERTION_UNAVAILABLE`, never an
unrestricted assertion — an empty `SiteCodes` list would be a credential for
every tenant on the VPS. A non-https `DATAHUB_API_BASE_URL` is omitted from the
claim rather than signed, because the validator rejects any other scheme and a
claim it rejects fails the whole enrollment.

`DATAHUB_LICENSE_ASSERTION_ISSUER` and `_AUDIENCE` must equal the VPS values
exactly (`backend/datahub/env.production.template`; staging uses the `-staging`
suffix). Leaving them unset falls back to the unsuffixed `autojms-license` /
`autojms-datahub-enroll`, which match **neither** template and fail every
enrollment.

## Google Sheets grant

Not tier-gated: BASE stations get a token too (owner decision). The lifecycle
gate still applies, and every rejection happens before Google is contacted, so
a refused request mints nothing. A Google outage is 503
`GOOGLE_SHEETS_BROKER_UNAVAILABLE`; a Firebase timeout is 503
`GOOGLE_SHEETS_TIMEOUT`. A license with no spreadsheet configured gets 200 with
`spreadsheetId: ""` — the station simply has nowhere to write.

## Logout

`POST /api/logout` requires the access token that owns the session it is
ending; it used to be unauthenticated and unmetered, so anyone who could reach
the host could burn Firebase writes, and anyone who learned a session id — from
a log line, a crash dump, a shared screenshot — could end that station's
session. Ending someone else's session is 403 `SESSION_MISMATCH`. A request
with no `sid` answers 200 without authentication and writes nothing: there is
nothing to authorise, and the client calls this on a shutdown path where a 401
would surface as a spurious error dialog.

## Session reaping

Logout is the only clean exit, and it removes exactly the one session it holds a
token for. verify-license removes the previous session for the **same** device.
Nothing removed anything else — so a session that ended any other way (crash,
power cut, laptop closed, station reimaged onto a new hwid) stayed in
`/sessions` forever. Those rows are what every login scans and what makes a
seats count read high.

verify-license now also deletes this licence's orphans while it is already
holding the answer: same query, same multi-path update, no background timer and
no extra read. A login with nothing to revoke but something to reap spends the
one write the flow would otherwise have skipped.

**Stale is derived, not chosen.** `lastPing` is written by exactly one route,
`/api/heartbeat`, and that route both requires an unexpired access token and
mints a fresh one. So a session silent for longer than one token TTL cannot have
heartbeated within the lifetime of any token it could still hold — its next
heartbeat is a 401 whatever this code does. The threshold is **2 × TTL = 120
minutes**, one full TTL of slack for clock skew.

That margin matters because deleting a row is not a soft action: the heartbeat
answers a missing session with 401 `action: "kill"` and the station shuts itself
down. A reaper that is merely approximately right ends a working shift. Hence
the rules `session-reaper.test.js` pins, all of which are about what survives:

- A row inside the threshold survives, even by one second.
- A row whose age **cannot be established** survives. `Number(null)` and
  `Number("")` are both `0` — 1 January 1970, maximally stale — so reading an
  absent field through `Number()` would delete the row it could not date. Only a
  finite positive number counts as a timestamp.
- `createdAt` stands in when `lastPing` is absent, which is the
  crashed-before-first-heartbeat case; a fresh `lastPing` overrides an ancient
  `createdAt`, so the longest-running stations are not the first reaped.
- The query is scoped by `licenseKey`, so one customer's login never touches
  another's rows. That scope is the blast radius, and it is pinned.

`revokedCount` and `reapedCount` are logged apart on purpose: repeated revokes
mean a station is re-verifying instead of resuming, repeated reaps mean stations
are dying without logging out. One merged number would hide both.

## Tests

    npm test

`node --test "test/**/*.test.js"` — 164 tests across 16 files, no network and no
Firebase project. `test/helpers/harness.js` boots the real `server.js`
in-process by injecting fakes into `require.cache` before requiring it;
`server.js` guards its `listen` with `require.main === module`, so production
behaviour is unchanged.

Two things about the suite that are easy to break:

- **The harness env list is exhaustive on purpose.** Every variable server.js
  reads is listed, including the ones whose intended value is "unset", because
  a second boot in the same process otherwise inherits whatever the first one
  set. A leaked `DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY` makes the
  "enrollment is closed" test pass for the wrong reason.
- **Files are split by rate-limiter budget.** `node --test` runs each *file* in
  its own process, so each file gets its own limiter budget. That is why
  verify-license is two files, and why `rate-limit.test.js` is on its own — its
  last test deliberately exhausts the global budget. Split a file rather than
  raising a production limit for the tests' benefit.
- **`activeSession()` is dated live, not fixed.** It used to seed a hard-coded
  November 2023 `lastPing`, which the reaper correctly reads as long dead — a
  fixture named "active" describing a corpse. Any new fixture that stands for a
  running station has to carry timestamps relative to `Date.now()`.

The fake Firebase records reads, writes and removals, because the assertions
that matter most are absences: no database read happened, no Google token was
minted, the session survived.

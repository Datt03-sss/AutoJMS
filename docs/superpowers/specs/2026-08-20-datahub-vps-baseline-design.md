# AutoJMS DataHub VPS Baseline Design

> Status: owner-approved architecture baseline, 2026-08-20.
> This document is the implementation source of truth for the new VPS data path.
> Older Supabase/DataHub planning documents remain historical and must not be used
> to generate the phase-1 SQL or API contract.

## Goal

Provide a durable, tenant-isolated dashboard backend for multiple AutoJMS clients that
are not on the same LAN, while keeping the JMS credential local to each Windows machine.
The first deployment runs on one existing VPS and can be rebuilt from encrypted offsite
backups plus replayable JMS observations.

## Non-goals for phase 1

- No database or schema per site.
- No direct PostgreSQL connection from desktop clients.
- No Supabase DataHub dependency.
- No Redis, Kafka, PgBouncer, partitioning, Kubernetes, or chat/note tables before those
  features exist.
- No Strict leader command queue. Interactive JMS calls remain local to the operator's
  machine.

## Runtime architecture

```text
Client (WinForms/WebView2)        Windows Service (per machine)
        | Named Pipe (local)               | JMS token in DPAPI
        | REST + SignalR                   | bulk/interactive JMS
        +---------------- HTTPS/WSS --------+
                         |
                  Caddy :443
                         |
             ASP.NET Core DataHub API
             + SignalR + maintenance job
                         |
                    PostgreSQL
                 (private Docker network)
```

Only Caddy publishes ports 80/443. API and PostgreSQL are internal Compose services.
PostgreSQL is never exposed on host port 5432.

Recommended implementation stack:

- ASP.NET Core 10 Web API and SignalR for the new server project.
- Npgsql with one shared `NpgsqlDataSource`; Dapper or explicit SQL for the ingest path.
- PostgreSQL 17 or the pinned supported major selected during deployment.
- Docker Compose, Caddy, and an encrypted object-storage backup target.

The existing .NET 8 desktop remains a client of this API; it does not need to target the
same framework version.

## Authority and data classes

```text
jms_observation  Windows Service <- JMS; replayable while JMS history is available
user_input       operator/dashboard; durable and non-disposable
derived          reducer output; rebuildable from observations
audit_config     API-managed configuration/audit; durable and non-disposable
```

The VPS is not described as a universal source of truth. Windows Service is the source
of JMS observations. PostgreSQL is the source of truth for user input, configuration,
and audit data. Projections are derived read models.

## Tenant and identity rules

- `sites.id` is an immutable UUID (or the existing immutable ID type if the deployment
  already has one). `site_code` is unique and may change.
- Every tenant-owned foreign key uses `site_id`, never `site_code` alone.
- The API derives `site_id` from the authenticated device token. A path site ID must
  match the token claim; a body field cannot grant access.
- A device token is separate from the JMS token. It contains `device_id`, `site_id`,
  `role`, and `token_version` and is revocable independently.
- SignalR connections join `site:{site_id}` based on server-side claims.

## Phase-1 schema

Required tables:

```text
sites
devices
site_fetch_leases
site_change_counters
waybill_scan_events
waybill_projections
dashboard_changes
jms_event_policies
idempotency_records
retention_policies
audit_logs
```

`user_notes`, `chat_messages`, and other user feature tables are added only when the
feature is implemented.

### Observation

`waybill_scan_events` is append-only except for retention. Its business timestamp is
`event_occurred_at`, parsed from JMS `scanTime`.

```sql
CREATE TABLE waybill_scan_events (
    id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    site_id             uuid NOT NULL REFERENCES sites(id),
    waybill_no          text NOT NULL,
    event_fingerprint   text NOT NULL,
    fingerprint_version smallint NOT NULL DEFAULT 1,
    event_occurred_at   timestamptz NOT NULL,
    ingested_at         timestamptz NOT NULL DEFAULT now(),
    scan_type_code      integer,
    scan_type_name      text,
    status              text,
    network_code        text,
    operator_code       text,
    package_number      text,
    task_code           text,
    payload             jsonb NOT NULL,
    UNIQUE (site_id, event_fingerprint)
);
CREATE INDEX ix_waybill_scan_events_site_waybill_time
    ON waybill_scan_events (site_id, waybill_no, event_occurred_at);
```

`uploadTime` may remain inside `payload` for diagnostics, but it is not a hot column,
index key, fingerprint input, reducer input, or retention clock.

### Projection

The projection has independent state and activity slots. An unknown JMS code defaults to
`activity` and cannot overwrite state.

```sql
CREATE TABLE waybill_projections (
    site_id                    uuid NOT NULL REFERENCES sites(id),
    waybill_no                 text NOT NULL,
    state_code                 integer,
    state_name                 text,
    state_event_at             timestamptz,
    state_fingerprint          text,
    state_event_id             bigint,
    last_activity_code         integer,
    last_activity_name         text,
    last_activity_kind         text,
    last_activity_at           timestamptz,
    last_activity_fingerprint  text,
    last_activity_event_id     bigint,
    inventory_event_at         timestamptz,
    inventory_fingerprint      text,
    inventory_event_id         bigint,
    reducer_version            integer NOT NULL DEFAULT 1,
    version                    bigint NOT NULL DEFAULT 1,
    updated_at                 timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (site_id, waybill_no)
);
```

Each slot uses the deterministic winner key:

```text
(event_occurred_at, event_fingerprint)
```

`ingested_at` and `uploadTime` never participate. If JMS later supplies a stable source
event ID, it is added to the canonical fingerprint in a new fingerprint version.

### Policy and change cursor

```sql
CREATE TABLE jms_event_policies (
    reducer_version integer NOT NULL,
    scan_type_code  integer NOT NULL,
    event_kind      text NOT NULL CHECK (event_kind IN
                    ('state_transition', 'activity', 'inventory', 'communication')),
    PRIMARY KEY (reducer_version, scan_type_code)
);

CREATE TABLE site_change_counters (
    site_id     uuid PRIMARY KEY REFERENCES sites(id),
    change_seq  bigint NOT NULL DEFAULT 0
);

CREATE TABLE dashboard_changes (
    site_id      uuid NOT NULL REFERENCES sites(id),
    change_seq   bigint NOT NULL,
    entity_type  text NOT NULL,
    entity_key   text NOT NULL,
    operation    text NOT NULL CHECK (operation IN ('upsert', 'delete', 'resync')),
    change_at    timestamptz NOT NULL DEFAULT now(),
    body         jsonb,
    PRIMARY KEY (site_id, change_seq)
);
CREATE INDEX ix_dashboard_changes_site_seq
    ON dashboard_changes (site_id, change_seq);
```

Every change is appended through one repository/database function that locks the
site counter, allocates the next number, inserts the change, and commits in the same
transaction as the projection update. No other code may insert directly into
`dashboard_changes`. The cursor is per-site and no API rule assumes numeric contiguity.

## Time parsing contract

The parser never uses the VPS timezone, `TimeZoneInfo.Local`, or `DateTime.Now`.

```text
SCAN_TIME_ZONE = Asia/Ho_Chi_Minh

yyyy-MM-dd HH:mm:ss with no offset:
  interpret as Asia/Ho_Chi_Minh, then convert to UTC

ISO value with Z or an explicit offset:
  honor the supplied offset; do not add +07:00

empty or invalid value:
  reject that item and report a parse error; never substitute current time
```

The first live canary must capture 3-5 JMS payloads and record the observed format. A
format deviation is observable and does not silently change the parser contract.

## Producer and leader contract

There are two producer paths:

| Path | Caller | Endpoint | Fencing |
|---|---|---|---|
| Bulk dashboard fetch | Windows Service holding site lease | `/jms/ingest` | Required |
| Interactive operation | Windows Service on operator machine | `/jms/observations` | Not required |

The UI never bulk-polls JMS. Interactive UI commands use a local ACL-protected Named
Pipe to the Windows Service; the JMS token stays in the service's DPAPI store.

Lease defaults:

```text
duration: 120 seconds
renew:     30 seconds
```

`site_fetch_leases` is seeded when a site is created and is never deleted:

```text
leader_device_id: NULL
leader_term:      0
lease_expires_at: -infinity
last_seen_at:     NULL
```

Acquire, renew, steal, and release are serialized with a row lock. Stealing an expired
lease increments `leader_term`; renew does not. Bulk ingest requires matching device,
term, and unexpired lease. A stale request receives `409 LEADER_FENCED`. API/network
failure pauses bulk ingestion; it never grants a local fail-open lease.

## Ingest transaction

For every bounded chunk (maximum 1 MB or 200 items):

```text
authenticate device and site
validate bulk fence when kind=bulk
validate idempotency key and request hash
parse scanTime
insert each event with ON CONFLICT DO NOTHING
reduce every affected waybill and every projection slot
lock site_change_counters and append changes for changed projections
commit
send a small SignalR doorbell after commit
```

Fingerprint v1 canonicalizes fixed fields, including `scanTime`, code/status, scan type,
network/operator/package/task fields, and `remark1..remark9`; it excludes `uploadTime`.
Same idempotency key with a different request hash returns `409 IDEMPOTENCY_KEY_REUSED`.

## Snapshot, delta, and realtime

```text
SignalR doorbell -> GET /changes?after=cursor -> apply SQLite transactionally
```

SignalR payload is only `{siteId, changeSeq, entityType, entityKey}`. On reconnect or a
missed notification, the client performs delta catch-up. A safety pull runs every
30-60 seconds.

Snapshot returns a `snapshot_seq` captured with the projection read. The client applies
the snapshot, sets its cursor to that watermark, and then requests changes after it.
The implementation must either stream all pages within one repeatable-read transaction
or issue a server snapshot token with a short TTL; separate unwatermarked page queries
are not permitted.

## Retention and recovery

Retention is data in `retention_policies`, not hard-coded application constants. Initial
defaults are:

```text
waybill_scan_events:  delete after 60 days, only after JMS replay is measured
waybill_projections:  retain while non-terminal; terminal cleanup is policy-driven
dashboard_changes:    retain at least 14 days plus maximum offline window
audit_logs:           default 90 days, configurable
sites/devices/config: no automatic deletion
```

Deleting a projection or change visible to offline clients requires a tombstone change
retained for at least the maximum supported offline period.

Backups are encrypted and stored outside the VPS. They must include user/config/audit/
device/site data. Observation dumps remain mandatory until the JMS replay window has
been measured. A daily dump implies an explicit maximum RPO of 24 hours; it is not HA.
Restore drill: before canary and monthly thereafter.

## Phase-1 API surface

```text
POST /api/v1/devices/enroll
POST /api/v1/sites/{siteId}/lease/acquire
POST /api/v1/sites/{siteId}/lease/renew
POST /api/v1/sites/{siteId}/lease/release
POST /api/v1/sites/{siteId}/jms/ingest
POST /api/v1/sites/{siteId}/jms/observations
GET  /api/v1/sites/{siteId}/changes?after=&limit=
GET  /api/v1/sites/{siteId}/projections/snapshot
GET  /health/live
GET  /health/ready
HUB  /hubs/site
```

All endpoints derive site authorization from the device token. The API does not expose
PostgreSQL credentials or raw JMS tokens.

## Cutover and verification

1. Deploy API/PostgreSQL/Caddy in staging.
2. Add parser and policy tests using live-format payload fixtures.
3. Install the Windows Service on one canary site and test Named Pipe/DPAPI.
4. Run bulk and interactive observations through the new pipeline while the old read
   path remains active.
5. Verify duplicate batches, delayed events, unknown codes, concurrent leader election,
   old-term fencing, reconnect/catch-up, snapshot watermark, and VPS restart.
6. Run the documented drop/restore/replay drill.
7. Switch one site's dashboard reads to the new API, then expand site by site.

The first implementation must not modify protected licensing, update, or WinForms
designer files. Client integration is an adapter change after the backend contract and
canary tests pass.

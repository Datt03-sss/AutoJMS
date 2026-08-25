# DataHub API contract

`datahub-v1.yaml` is the phase-1 HTTP and SignalR contract for the new VPS
DataHub. It is derived from the owner-approved VPS baseline. Historical DataHub
documents are not inputs to this contract.

## Environments

There are two independent deployments:

| Environment | URL | API setting |
| --- | --- | --- |
| Staging/dev | `https://datahub-dev.example.com` | `DATAHUB_CHANNEL=staging` |
| Production | `https://datahub.example.com` | `DATAHUB_CHANNEL=production` |

Each deployment has its own PostgreSQL volume, JWT issuer/audience and signing
keys, enrollment pepper, site/device rows, and encrypted backup bucket. A staging
device token is not valid in production. The same image may be promoted after the
staging checks pass, but data and credentials are never shared. Replacing a VPS
means restoring its backup and moving the stable DNS name; the license does not
contain an IP address.

## Authentication and channel binding

1. `POST /api/v1/devices/enroll` accepts one signed license assertion in the
   `Authorization` bearer value. The assertion must contain `channel` (`staging`
   or `production`), `site_codes`, and `exp`. It may contain a signed HTTPS
   `datahub_url` endpoint override. The API checks the signature and expiry,
   requires the channel to equal its `DATAHUB_CHANNEL`, and requires the requested
   existing `siteCode` to be in `site_codes`.
   Production assertions are JWS values verified by the asymmetric license
   authority. The current `v1.<base64url-json>.<HMAC>` issuer is an
   integration-only staging seam; production readiness stays red until the JWS
   verifier is installed.
2. Enrollment returns a derived device bearer token. All later lease, JMS, delta,
   snapshot, and SignalR calls use that token. A license assertion is never an API
   credential after enrollment; a JMS auth token is never an API credential.
3. Device claims bind `device_id`, `site_id`, `channel`, role, and token version.
   A path `siteId` must equal the claim. The server derives the SignalR group from
   the claim.

`channel` and site values in arbitrary request bodies or headers are not authority.
The API ignores them for authorization and returns `CHANNEL_MISMATCH` when the
signed/derived channel does not match the deployment, or `SITE_NOT_LICENSED` when
the signed license does not include the requested site. Clients must not expose a
free-form host field to operators; `datahub_url` is accepted only as a signed
license claim and must be HTTPS.

## Ingest contract

`/jms/ingest` and `/jms/observations` call one semantic `IngestPipeline`:

```text
authenticate -> validate request hash/idempotency -> parse scanTime
-> fingerprint -> insert observation -> reduce state/activity/inventory slots
-> allocate site change sequence and append body -> commit -> SignalR doorbell
```

The bulk route is used by the Windows Service holding the site lease and requires
`X-Leader-Term`. The interactive route is used by the service on the operator's
machine and has no lease fence. Both routes require `Authorization` and
`Idempotency-Key`. A key is bound to the SHA-256 body hash for 24 hours; reusing a
key with another body returns `409 IDEMPOTENCY_KEY_REUSED`.

Each request is limited to 1 MiB and 200 items. The service chunks a larger JMS
response and creates one idempotency key per chunk. The top-level observation is a
normalized object. The raw JMS object is retained under `payload`; `uploadTime` is
allowed only there and is never a hot column, index, fingerprint input, reducer
ordering value, or retention clock.

`scanTime` is the business time. A naive `yyyy-MM-dd HH:mm:ss` value is interpreted
as `Asia/Ho_Chi_Minh` and stored as UTC. ISO values carrying `Z` or an explicit
offset use that offset. Empty or invalid values fail the item with
`400 INVALID_SCAN_TIME`; the API never substitutes server-local time.

The reducer maintains independent winners keyed by
`(event_occurred_at, event_fingerprint)`:

- `current_state_*`: only `state_transition` policy events;
- `latest_activity_*`: every event kind, including communication and inventory;
- `inventory_*`: only `inventory` policy events.

Unknown codes default to activity. `ingested_at` and `uploadTime` never decide a
winner. The `dashboard_changes.body` for an upsert is the complete compact hot
projection snapshot, so a client can apply a delta without fetching each key.

## Delta, snapshot, and SignalR

`GET /api/v1/sites/{siteId}/changes?after=N&limit=500` returns rows with
`changeSeq > N` for that site. The cursor is per-site and allocated by a locked
counter in the same transaction as the projection update. Clients must not require
`N + 1`; the HTTP response is authoritative.

`GET /api/v1/sites/{siteId}/projections/snapshot?limit=5000` is a phase-1 buffered
response: the API keeps one PostgreSQL `REPEATABLE READ` transaction, captures one
`snapshot_seq`, reads the site's ordered projection set, commits, and returns that
watermark with the projection rows. After applying the response, the client sets
its cursor to `snapshot_seq` and reads `/changes?after=snapshot_seq`. Snapshot
tokens, streaming, and multi-request paging are intentionally deferred; staging
must measure snapshot size and latency before production canary.

`limit` defaults to 5000 and is capped at 10000. It exists because the client has
always sent it while the server had no such parameter, so model binding discarded
it and every snapshot returned the site's entire projection table in one body. When
a site holds more rows than the limit, the response sets `truncated: true` and the
server logs a warning: `items` is then a prefix ordered by waybill number, and a
client that adopts `snapshot_seq` as its cursor anyway loses the remainder until
those waybills next change. Both `limit` parameters reject an out-of-range value
with `400 BAD_REQUEST` instead of clamping — clamping told a client asking for 2000
nothing about the 500 it actually got.

The SignalR hub is `/hubs/site` (with the normal `/hubs/site/negotiate` endpoint).
After commit it sends only this doorbell message:

```json
{
  "siteId": "00000000-0000-0000-0000-000000000000",
  "changeSeq": 42,
  "entityType": "waybill_projection",
  "entityKey": "862229607222"
}
```

The message contains no projection data. On connect, reconnect, missed messages,
or the 30-60 second safety pull, fetch HTTP changes from the local cursor. The
server chooses the `site:{siteId}` group from claims; a client cannot subscribe to
another site.

## Control plane

The same host serves the published objects the desktop reads before it has any
credential at all. Four containers are allowlisted, and the path allowlist is the
entire perimeter:

| Container | Read by | Typical objects |
| --- | --- | --- |
| `/manifest/` | `VpsManifestService`, `VpsRuntimePolicyService` | `version-latest.json`, `tier-definitions.json`, `feature-policy.{tier}.json` |
| `/configs/` | `VpsRuntimePolicyService`, `VpsConfig` | `runtime-policy.json`, `runtime-policy.{tier}.json`, `runtime-config.enc` |
| `/selector-updates/` | `SmallUpdateService` | encrypted payload plus detached signature |
| `/modules/` | `VpsModuleProvider` | module blobs |

Reads are anonymous by necessity: a station must read tier definitions and the
update manifest before enrollment, and `VpsManifestService` fetches with a bare
`HttpClient`. **A published object must therefore never contain a secret.**

Both `GET` and `HEAD` are served. Nothing in the desktop sends `HEAD` — it only
calls `GetStringAsync` — but the publish script verifies its own work that way, and
an operator asking "is the policy actually published?" should not have to download
it. Responses carry a strong ETag (quoted lowercase SHA-256 of the content) and
`Cache-Control: public, max-age=60, must-revalidate`, so a policy change reaches
the fleet within minutes without a restart storm re-downloading every object.
`If-None-Match` is honoured, including a `W/`-weakened tag.

Any unservable path — wrong container, too many segments, a traversal attempt —
answers the same `404 NOT_FOUND` as a missing object. An anonymous caller learns
whether a path is servable, never why a rejected one failed.

`PUT /api/v1/admin/manifests/{objectPath}` publishes. It requires the operator
bearer token (`DATAHUB_ADMIN_TOKEN`, `AdminBearer`); a device token is explicitly
not sufficient, because an enrolled station is a customer machine, not the
publisher. With no token configured the route answers `503` rather than accepting
anything. `201` for a new path, `200` for a replacement, both returning
`{ objectPath, etag, length }` and an `ETag` header. A `.json` object must parse
before it is stored — comments and trailing commas are rejected here even though
the desktop's own reader tolerates them, so a seed that would parse on a station
can still be refused at publish time. Objects are capped at 1 MiB and the route is
limited to 30 requests/minute/IP; a release publishes about a dozen objects.
Publishes are audited to the application log rather than `audit_logs`, because that
table needs a PostgreSQL transaction and a publish must still work while the
database is down.

## Errors

All API errors use `application/problem+json` with `code` and `traceId`. The
contract defines these important outcomes:

| Status | Code | Meaning |
| ---: | --- | --- |
| 400 | `INVALID_SCAN_TIME` | JMS `scanTime` cannot be parsed |
| 401 | `UNAUTHORIZED` | Missing or invalid bearer credential |
| 403 | `CHANNEL_MISMATCH` | Signed/device channel differs from `DATAHUB_CHANNEL` |
| 403 | `SITE_NOT_LICENSED` | Requested site is outside signed `site_codes` |
| 404 | `NOT_FOUND` | Site/device/resource does not exist |
| 409 | `LEADER_FENCED` | Bulk term/device/lease is stale |
| 409 | `LEASE_HELD` | Another device holds an unexpired lease |
| 409 | `SEAT_LIMIT_REACHED` | Signed license has no available active device seat |
| 409 | `DEVICE_CONFLICT` | Device identity conflicts with a non-active record |
| 409 | `IDEMPOTENCY_KEY_REUSED` | Same key has a different body hash |
| 409 | `IDEMPOTENCY_IN_PROGRESS` | Same key is currently being processed |
| 409 | `RESYNC_REQUIRED` | Cursor is older than retained changes |
| 413 | `PAYLOAD_TOO_LARGE` | Body exceeds 1 MiB or 200 items |
| 422 | `VALIDATION_FAILED` | Schema or domain validation failed |
| 429 | `RATE_LIMITED` | Caller exceeded a bounded rate |
| 503 | `SERVICE_UNAVAILABLE` | API/PostgreSQL dependency is unavailable |

Control-plane failures reuse `BAD_REQUEST` rather than adding codes: a rejected
object path and an over-1-MiB body both answer `BAD_REQUEST` (at 400 and 413
respectively). `PAYLOAD_TOO_LARGE` stays reserved for the ingest item/body
contract, where a client distinguishes it to decide whether to split a batch.

`/health/live` is process liveness and does not require PostgreSQL. `/health/ready`
returns 503 when PostgreSQL, required secrets, or channel configuration is not
ready; it must never fall back to another environment.

## Linting

Run the deterministic contract check from the repository root:

```powershell
pwsh .\backend\datahub\openapi\openapi-lint.ps1
```

The default path always performs deterministic static contract checks for required
routes, security schemes, headers, limits, errors, cursor/snapshot semantics, and
the absence of an `uploadTime` hot field. CI can add `-RequireFullLinter` after
installing a pinned local Redocly CLI (or package for `npx --no-install`). The
script never downloads packages implicitly; a non-zero exit blocks the API build.

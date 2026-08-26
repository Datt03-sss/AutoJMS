-- Device-credential revocation, plus the three indexes retention actually needs.
--
-- On the name: the plan calls this table `jti_cache`. It is not one, because a DataHub
-- device token has no jti to cache. HmacDeviceTokenService signs
-- {deviceId, siteId, channel, role, tokenVersion, expiresAt, issuer, audience} and
-- nothing else, so a jti column here could never be filled by any code path — a table
-- that looks like replay protection and is structurally incapable of providing it. The
-- jti cache in the plan belongs to the Render license server (server.js:293), a separate
-- process with a separate token format.
--
-- What DataHub can revoke is what it already computes on every request: the credential
-- digest HMAC(enrollment pepper, bearer token), which DeviceAuthenticationMiddleware puts
-- in HttpContext.Items and DeviceRepository.TouchActiveAsync matches against
-- devices.credential_hash. Revocation today means bumping token_version by re-enrolling,
-- which invalidates the whole device; this table is the missing narrower tool — retire one
-- leaked token without disturbing the station that is still using its replacement.
--
-- Schema only. No code reads this table yet: TouchActiveAsync gains an AND NOT EXISTS in a
-- later change, and until it does, inserting a row here revokes nothing.
--
-- Transactional on purpose (no `-- no-transaction` marker). CREATE INDEX takes a SHARE
-- lock that blocks writes for its duration, which is acceptable only because these tables
-- are empty-to-tiny on both staging and production today. Re-running this against a site
-- with real history must go through the `_notx` form with CREATE INDEX CONCURRENTLY
-- instead — see the CONCURRENTLY warning in the deploy plan.

CREATE TABLE IF NOT EXISTS revoked_device_credentials (
    credential_hash text PRIMARY KEY,
    device_id       uuid NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    site_id         uuid NOT NULL REFERENCES sites(id),
    token_version   integer NOT NULL,
    reason          text NOT NULL DEFAULT 'manual',
    revoked_at      timestamptz NOT NULL DEFAULT now(),
    -- The revoked token's own expiry. Past it the token is refused by signature checking
    -- anyway, so the row stops carrying information and becomes retention-eligible.
    expires_at      timestamptz NOT NULL,
    CONSTRAINT ck_revoked_device_credentials_hash_not_blank CHECK (length(btrim(credential_hash)) > 0),
    CONSTRAINT ck_revoked_device_credentials_reason_not_blank CHECK (length(btrim(reason)) > 0),
    CONSTRAINT ck_revoked_device_credentials_token_version_positive CHECK (token_version > 0)
);

-- Mirrors ix_idempotency_records_expiry: the sweeper deletes by expiry, nothing else.
CREATE INDEX IF NOT EXISTS ix_revoked_device_credentials_expiry
    ON revoked_device_credentials (expires_at);

-- RetentionRepository.DeleteChangesAsync aggregates every dashboard_changes row per site,
-- computing max(change_seq) and min(change_seq) FILTER (change_at >= cutoff). change_seq is
-- the third column so that aggregate can be answered index-only instead of by heap scan;
-- the leading (site_id, change_at) pair is what the plan asked for and is a prefix of this.
CREATE INDEX IF NOT EXISTS ix_dashboard_changes_site_change_at
    ON dashboard_changes (site_id, change_at, change_seq);

-- DeleteEventsAsync filters on (site_id, event_occurred_at). The existing
-- ix_waybill_scan_events_site_waybill_time leads with (site_id, waybill_no), so it cannot
-- serve a predicate that names no waybill.
CREATE INDEX IF NOT EXISTS ix_waybill_scan_events_site_occurred
    ON waybill_scan_events (site_id, event_occurred_at);

-- DeleteAuditLogsAsync scans across all sites: WHERE a.at < cutoff ORDER BY a.at, a.id.
-- ix_audit_logs_site_at leads with site_id and is useless for that; this matches both the
-- predicate and the sort, so the LIMIT stops early instead of after a full sort.
CREATE INDEX IF NOT EXISTS ix_audit_logs_retention
    ON audit_logs (at, id);

INSERT INTO schema_migrations (version)
VALUES ('006_revocation_and_retention_indexes')
ON CONFLICT (version) DO NOTHING;

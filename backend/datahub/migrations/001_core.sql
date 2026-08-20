BEGIN;

-- The migration is intentionally self-contained and safe to apply before the API
-- starts. Environment/channel enforcement belongs to the signed-token boundary.

CREATE TABLE IF NOT EXISTS sites (
    id         uuid PRIMARY KEY,
    site_code  text NOT NULL UNIQUE,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_sites_site_code_not_blank CHECK (length(btrim(site_code)) > 0)
);

CREATE TABLE IF NOT EXISTS devices (
    id              uuid PRIMARY KEY,
    site_id         uuid NOT NULL REFERENCES sites(id),
    name            text NOT NULL,
    credential_hash text NOT NULL,
    token_version   integer NOT NULL DEFAULT 1,
    status          text NOT NULL DEFAULT 'active',
    last_seen_at    timestamptz,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_devices_name_not_blank CHECK (length(btrim(name)) > 0),
    CONSTRAINT ck_devices_credential_hash_not_blank CHECK (length(btrim(credential_hash)) > 0),
    CONSTRAINT ck_devices_token_version_positive CHECK (token_version > 0),
    CONSTRAINT ck_devices_status CHECK (status IN ('active', 'revoked', 'disabled')),
    CONSTRAINT uq_devices_site_name UNIQUE (site_id, name)
);

CREATE TABLE IF NOT EXISTS site_fetch_leases (
    site_id           uuid PRIMARY KEY REFERENCES sites(id),
    leader_device_id  uuid NULL REFERENCES devices(id),
    leader_term       bigint NOT NULL DEFAULT 0,
    lease_expires_at  timestamptz NOT NULL DEFAULT '-infinity'::timestamptz,
    last_seen_at      timestamptz NULL,
    CONSTRAINT ck_site_fetch_leases_term_nonnegative CHECK (leader_term >= 0)
);

CREATE TABLE IF NOT EXISTS site_change_counters (
    site_id     uuid PRIMARY KEY REFERENCES sites(id),
    change_seq  bigint NOT NULL DEFAULT 0,
    CONSTRAINT ck_site_change_counters_seq_nonnegative CHECK (change_seq >= 0)
);

CREATE TABLE IF NOT EXISTS waybill_scan_events (
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
    CONSTRAINT ck_waybill_events_waybill_not_blank CHECK (length(btrim(waybill_no)) > 0),
    CONSTRAINT ck_waybill_events_fingerprint_not_blank CHECK (length(btrim(event_fingerprint)) > 0),
    CONSTRAINT ck_waybill_events_fingerprint_version_positive CHECK (fingerprint_version > 0),
    CONSTRAINT uq_waybill_events_site_fingerprint UNIQUE (site_id, event_fingerprint)
);

CREATE INDEX IF NOT EXISTS ix_waybill_scan_events_site_waybill_time
    ON waybill_scan_events (site_id, waybill_no, event_occurred_at);

CREATE TABLE IF NOT EXISTS waybill_projections (
    site_id                    uuid NOT NULL REFERENCES sites(id),
    waybill_no                 text NOT NULL,

    state_code                 integer,
    state_name                 text,
    state_event_at             timestamptz,
    state_fingerprint          text,
    state_event_id             bigint,
    state_kind                 text,

    last_activity_code         integer,
    last_activity_name         text,
    last_activity_kind         text,
    last_activity_at           timestamptz,
    last_activity_fingerprint  text,
    last_activity_event_id     bigint,

    inventory_code             integer,
    inventory_name             text,
    inventory_event_at         timestamptz,
    inventory_fingerprint      text,
    inventory_event_id         bigint,

    payload                    jsonb NOT NULL DEFAULT '{}'::jsonb,
    reducer_version            integer NOT NULL DEFAULT 1,
    version                    bigint NOT NULL DEFAULT 1,
    updated_at                 timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT ck_waybill_projections_waybill_not_blank CHECK (length(btrim(waybill_no)) > 0),
    CONSTRAINT ck_waybill_projections_reducer_version_positive CHECK (reducer_version > 0),
    CONSTRAINT ck_waybill_projections_version_positive CHECK (version > 0),
    PRIMARY KEY (site_id, waybill_no)
);

-- Event IDs are optional hydration references. They intentionally have no FK: event
-- retention must never make a dashboard projection undeletable or unreadable.

CREATE TABLE IF NOT EXISTS jms_event_policies (
    reducer_version integer NOT NULL,
    scan_type_code  integer NOT NULL,
    event_kind      text NOT NULL,
    CONSTRAINT ck_jms_event_policies_kind CHECK (
        event_kind IN ('state_transition', 'activity', 'inventory', 'communication')
    ),
    PRIMARY KEY (reducer_version, scan_type_code)
);

CREATE TABLE IF NOT EXISTS dashboard_changes (
    site_id      uuid NOT NULL REFERENCES sites(id),
    change_seq   bigint NOT NULL,
    entity_type  text NOT NULL,
    entity_key   text NOT NULL,
    operation    text NOT NULL,
    change_at    timestamptz NOT NULL DEFAULT now(),
    body         jsonb NOT NULL DEFAULT '{}'::jsonb,
    CONSTRAINT ck_dashboard_changes_seq_positive CHECK (change_seq > 0),
    CONSTRAINT ck_dashboard_changes_operation CHECK (operation IN ('upsert', 'delete', 'resync')),
    PRIMARY KEY (site_id, change_seq)
);

CREATE TABLE IF NOT EXISTS idempotency_records (
    site_id       uuid NOT NULL REFERENCES sites(id),
    key           text NOT NULL,
    body_sha256   text NOT NULL,
    response      jsonb NOT NULL,
    status_code   integer NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),
    expires_at    timestamptz NOT NULL,
    CONSTRAINT ck_idempotency_key_not_blank CHECK (length(btrim(key)) > 0),
    CONSTRAINT ck_idempotency_body_hash_not_blank CHECK (length(btrim(body_sha256)) > 0),
    PRIMARY KEY (site_id, key)
);

CREATE TABLE IF NOT EXISTS retention_policies (
    id             bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    site_id        uuid NULL REFERENCES sites(id),
    table_name     text NOT NULL,
    clock_column   text NOT NULL,
    hot_after      interval,
    archive_after  interval,
    delete_after   interval,
    CONSTRAINT ck_retention_table_name_not_blank CHECK (length(btrim(table_name)) > 0),
    CONSTRAINT ck_retention_clock_column_not_blank CHECK (length(btrim(clock_column)) > 0),
    CONSTRAINT ck_retention_intervals_nonnegative CHECK (
        (hot_after IS NULL OR hot_after >= interval '0') AND
        (archive_after IS NULL OR archive_after >= interval '0') AND
        (delete_after IS NULL OR delete_after >= interval '0')
    )
);

-- NULL does not participate in normal UNIQUE equality in PostgreSQL, so use two
-- partial indexes to enforce one global policy and one policy per site/table.
CREATE UNIQUE INDEX IF NOT EXISTS ux_retention_policies_global_table
    ON retention_policies (table_name)
    WHERE site_id IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_retention_policies_site_table
    ON retention_policies (site_id, table_name)
    WHERE site_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS audit_logs (
    id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    site_id    uuid NULL REFERENCES sites(id),
    actor      text NOT NULL,
    action     text NOT NULL,
    at         timestamptz NOT NULL DEFAULT now(),
    payload    jsonb NOT NULL DEFAULT '{}'::jsonb,
    CONSTRAINT ck_audit_logs_actor_not_blank CHECK (length(btrim(actor)) > 0),
    CONSTRAINT ck_audit_logs_action_not_blank CHECK (length(btrim(action)) > 0)
);

CREATE INDEX IF NOT EXISTS ix_devices_site_status
    ON devices (site_id, status);
CREATE INDEX IF NOT EXISTS ix_idempotency_records_expiry
    ON idempotency_records (expires_at);
CREATE INDEX IF NOT EXISTS ix_audit_logs_site_at
    ON audit_logs (site_id, at);

-- This helper is called by site provisioning code. The function runs in the caller's
-- transaction, so site, lease, and cursor rows are committed atomically.
CREATE OR REPLACE FUNCTION create_datahub_site(
    p_site_id uuid,
    p_site_code text
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    INSERT INTO sites (id, site_code)
    VALUES (p_site_id, p_site_code);

    INSERT INTO site_fetch_leases (site_id)
    VALUES (p_site_id);

    INSERT INTO site_change_counters (site_id, change_seq)
    VALUES (p_site_id, 0);
END;
$$;

COMMIT;

-- 008_terminal_tombstone.sql
-- Terminal state on the projection, and the ledger that outlives it.
--
-- is_terminal is NOT NULL DEFAULT false rather than nullable: "we have not decided" is not
-- a state this column can be in, and a three-valued flag would make every reader write the
-- IS NOT TRUE form to stay correct. PostgreSQL 11+ stores a non-volatile default in the
-- catalogue, so this does not rewrite the table either.
--
-- waybill_tombstones is the anti-resurrection ledger: once the P6 purge removes a terminal
-- projection, the tombstone is the only remaining proof that the waybill existed and must
-- not come back from a late re-ingest. Nothing in P1 inserts into it -- rows are written by
-- the purge -- but ingest reads it from P1 onward, which is why it is created now.
--
-- It deliberately does NOT reference waybill_projections: the projection is exactly the row
-- that is gone by the time a tombstone matters. site_id references sites(id) with no
-- cascade, matching every other table in 001_core.
--
-- Transactional on purpose (no `-- no-transaction` marker).

ALTER TABLE waybill_projections
    ADD COLUMN IF NOT EXISTS is_terminal          boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS terminal_at          timestamptz,
    ADD COLUMN IF NOT EXISTS terminal_state_code  integer,
    ADD COLUMN IF NOT EXISTS last_change_seq      bigint;

-- A terminal row without a timestamp cannot be aged out by the P6 purge, so it would sit
-- terminal forever and never become eligible. Cheap to enforce here, impossible to repair
-- later without guessing the time.
ALTER TABLE waybill_projections
    DROP CONSTRAINT IF EXISTS ck_waybill_projections_terminal_at_present;
ALTER TABLE waybill_projections
    ADD CONSTRAINT ck_waybill_projections_terminal_at_present
        CHECK (is_terminal = false OR terminal_at IS NOT NULL);

CREATE TABLE IF NOT EXISTS waybill_tombstones (
    site_id             uuid        NOT NULL REFERENCES sites(id),
    waybill_no          text        NOT NULL,
    terminal_at         timestamptz,
    terminal_state_code integer,
    last_change_seq     bigint,
    purged_at           timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (site_id, waybill_no)
);

INSERT INTO schema_migrations (version) VALUES ('008_terminal_tombstone')
ON CONFLICT (version) DO NOTHING;

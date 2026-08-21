-- Additive compatibility migration for databases that applied 001_core before
-- per-slot payload/status retention was introduced.
ALTER TABLE waybill_projections
    ADD COLUMN IF NOT EXISTS state_status text,
    ADD COLUMN IF NOT EXISTS state_payload jsonb NOT NULL DEFAULT '{}'::jsonb,
    ADD COLUMN IF NOT EXISTS last_activity_status text,
    ADD COLUMN IF NOT EXISTS last_activity_payload jsonb NOT NULL DEFAULT '{}'::jsonb,
    ADD COLUMN IF NOT EXISTS inventory_status text,
    ADD COLUMN IF NOT EXISTS inventory_payload jsonb NOT NULL DEFAULT '{}'::jsonb;

INSERT INTO schema_migrations (version)
VALUES ('004_projection_slot_payloads')
ON CONFLICT (version) DO NOTHING;

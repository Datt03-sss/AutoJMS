-- 007_event_metadata.sql
-- Metadata that later streaming phases read off an event without re-deriving it: which
-- reducer kind the event was classified as, and the three version numbers that say how it
-- was produced. Nothing in P1 writes them -- InsertEventAsync is untouched -- so every
-- column is nullable with no default, which is also what keeps this ALTER from rewriting a
-- table that already holds every scan the fleet has ever sent.
--
-- event_kind repeats the vocabulary of jms_event_policies.event_kind rather than
-- referencing it: the policy table is keyed by (reducer_version, scan_type_code) and says
-- what a code means *now*, while this column records what the event was taken to mean when
-- it was ingested. A foreign key would tie a historical fact to a mutable classification.
--
-- Transactional on purpose (no `-- no-transaction` marker). Only 009 needs CONCURRENTLY.

ALTER TABLE waybill_scan_events
    ADD COLUMN IF NOT EXISTS event_kind             text,
    ADD COLUMN IF NOT EXISTS reducer_version        integer,
    ADD COLUMN IF NOT EXISTS normalizer_version     integer,
    ADD COLUMN IF NOT EXISTS source_schema_version  integer;

-- Dropped first so a re-run cannot fail on "constraint already exists". The NULL arm is
-- what lets every existing row -- and every row P1 writes -- satisfy it.
ALTER TABLE waybill_scan_events
    DROP CONSTRAINT IF EXISTS ck_waybill_scan_events_event_kind;
ALTER TABLE waybill_scan_events
    ADD CONSTRAINT ck_waybill_scan_events_event_kind
        CHECK (event_kind IS NULL
               OR event_kind IN ('state_transition', 'activity', 'inventory', 'communication'));

INSERT INTO schema_migrations (version) VALUES ('007_event_metadata')
ON CONFLICT (version) DO NOTHING;

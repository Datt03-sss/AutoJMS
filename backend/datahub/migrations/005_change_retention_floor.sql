-- Record the highest committed cursor deliberately removed by retention. MIN()
-- cannot distinguish an intact history starting at 1 from a pruned history.
ALTER TABLE site_change_counters
    ADD COLUMN IF NOT EXISTS pruned_through_seq bigint NOT NULL DEFAULT 0;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
          FROM pg_constraint
         WHERE conrelid = 'site_change_counters'::regclass
           AND conname = 'ck_site_change_counters_pruned_range'
    ) THEN
        ALTER TABLE site_change_counters
            ADD CONSTRAINT ck_site_change_counters_pruned_range
            CHECK (pruned_through_seq >= 0 AND pruned_through_seq <= change_seq);
    END IF;
END
$$;

INSERT INTO schema_migrations (version)
VALUES ('005_change_retention_floor')
ON CONFLICT (version) DO NOTHING;

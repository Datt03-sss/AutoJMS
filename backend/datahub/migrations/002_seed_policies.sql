-- Seed only the codes explicitly classified by the approved contract. Unknown codes
-- remain activity at runtime until an operator adds a versioned policy row.
INSERT INTO jms_event_policies (reducer_version, scan_type_code, event_kind)
VALUES
    (1, 98, 'inventory'),
    (1, 110, 'state_transition')
ON CONFLICT (reducer_version, scan_type_code) DO NOTHING;

INSERT INTO schema_migrations (version)
VALUES ('002_seed_policies')
ON CONFLICT (version) DO NOTHING;

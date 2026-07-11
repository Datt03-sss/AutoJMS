namespace AutoJMS.FullStack.LocalDb
{
    public static class FullStackMigrations
    {
        public const int CurrentVersion = 2;

        public const string SchemaV1 = @"
CREATE TABLE IF NOT EXISTS fs_schema_version (
    version INTEGER PRIMARY KEY,
    applied_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS fs_inventory_runs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    action_site_code TEXT,
    start_date TEXT,
    end_date TEXT,
    started_at TEXT NOT NULL,
    finished_at TEXT,
    total_records INTEGER NOT NULL DEFAULT 0,
    total_pages INTEGER NOT NULL DEFAULT 0,
    status TEXT NOT NULL,
    error_message TEXT,
    source TEXT,
    created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS fs_waybills (
    waybill_no TEXT PRIMARY KEY,
    first_seen_at TEXT,
    last_seen_at TEXT,
    first_inventory_run_id INTEGER,
    last_inventory_run_id INTEGER,
    is_in_current_inventory INTEGER NOT NULL DEFAULT 1,
    left_inventory_at TEXT,
    current_state TEXT,
    current_status TEXT,
    last_action TEXT,
    last_action_time TEXT,
    last_site_code TEXT,
    last_site_name TEXT,
    employee_code TEXT,
    employee_name TEXT,
    receiver_name TEXT,
    receiver_phone_masked TEXT,
    age_hours REAL NOT NULL DEFAULT 0,
    days_in_inventory REAL NOT NULL DEFAULT 0,
    risk_score INTEGER NOT NULL DEFAULT 0,
    risk_level TEXT,
    risk_reasons TEXT,
    sla_status TEXT,
    sla_deadline TEXT,
    last_track_at TEXT,
    next_track_at TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    trang_thai_hien_tai TEXT,
    thao_tac_cuoi TEXT,
    thoi_gian_thao_tac TEXT,
    thoi_gian_yeu_cau_phat_lai TEXT,
    nhan_vien_kien_van_de TEXT,
    nguyen_nhan_kien_van_de TEXT,
    buu_cuc_thao_tac TEXT,
    nguoi_thao_tac TEXT,
    dau_chuyen_hoan TEXT,
    dia_chi_nhan_hang TEXT,
    phuong TEXT,
    noi_dung_hang_hoa TEXT,
    cod_thuc_te TEXT,
    pttt TEXT,
    nhan_vien_nhan_hang TEXT,
    dia_chi_lay_hang TEXT,
    thoi_gian_nhan_hang TEXT,
    ten_nguoi_gui TEXT,
    trong_luong TEXT,
    ma_doan_full TEXT,
    ma_doan_1 TEXT,
    ma_doan_2 TEXT,
    ma_doan_3 TEXT,
    reback_status TEXT,
    in_hoan_scan_time TEXT,
    print_count INTEGER NOT NULL DEFAULT 0,
    is_active INTEGER NOT NULL DEFAULT 1,
    tracking_interval_mins INTEGER NOT NULL DEFAULT 30,
    FOREIGN KEY(first_inventory_run_id) REFERENCES fs_inventory_runs(id),
    FOREIGN KEY(last_inventory_run_id) REFERENCES fs_inventory_runs(id)
);

CREATE TABLE IF NOT EXISTS fs_inventory_run_items (
    run_id INTEGER NOT NULL,
    waybill_no TEXT NOT NULL,
    page_no INTEGER,
    seen_at TEXT NOT NULL,
    PRIMARY KEY (run_id, waybill_no),
    FOREIGN KEY(run_id) REFERENCES fs_inventory_runs(id) ON DELETE CASCADE,
    FOREIGN KEY(waybill_no) REFERENCES fs_waybills(waybill_no) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS fs_tracking_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    waybill_no TEXT NOT NULL,
    event_time TEXT,
    action TEXT,
    status TEXT,
    site_code TEXT,
    site_name TEXT,
    operator_code TEXT,
    operator_name TEXT,
    raw_json TEXT,
    created_at TEXT NOT NULL,
    FOREIGN KEY(waybill_no) REFERENCES fs_waybills(waybill_no) ON DELETE CASCADE,
    UNIQUE(waybill_no, event_time, action, site_code)
);

CREATE TABLE IF NOT EXISTS fs_order_state_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    waybill_no TEXT NOT NULL,
    old_state TEXT,
    new_state TEXT,
    reason TEXT,
    changed_at TEXT NOT NULL,
    FOREIGN KEY(waybill_no) REFERENCES fs_waybills(waybill_no) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS fs_order_notes (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    waybill_no TEXT NOT NULL,
    note TEXT NOT NULL,
    created_by TEXT,
    created_at TEXT NOT NULL,
    FOREIGN KEY(waybill_no) REFERENCES fs_waybills(waybill_no) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS fs_order_checks (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    waybill_no TEXT NOT NULL,
    checked_at TEXT NOT NULL,
    checked_by TEXT,
    note TEXT,
    created_at TEXT NOT NULL,
    FOREIGN KEY(waybill_no) REFERENCES fs_waybills(waybill_no) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS fs_dispatch_tasks (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    waybill_no TEXT NOT NULL,
    task_type TEXT NOT NULL,
    priority INTEGER NOT NULL DEFAULT 0,
    status TEXT NOT NULL,
    assigned_to TEXT,
    due_at TEXT,
    created_at TEXT NOT NULL,
    completed_at TEXT,
    FOREIGN KEY(waybill_no) REFERENCES fs_waybills(waybill_no) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS fs_sync_state (
    key TEXT PRIMARY KEY,
    value TEXT,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS fs_settings (
    key TEXT PRIMARY KEY,
    value TEXT,
    updated_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_fs_waybills_current_inventory ON fs_waybills(is_in_current_inventory);
CREATE INDEX IF NOT EXISTS idx_fs_waybills_state ON fs_waybills(current_state);
CREATE INDEX IF NOT EXISTS idx_fs_waybills_risk ON fs_waybills(risk_level, risk_score);
CREATE INDEX IF NOT EXISTS idx_fs_waybills_last_seen ON fs_waybills(last_seen_at);
CREATE INDEX IF NOT EXISTS idx_fs_waybills_last_track ON fs_waybills(last_track_at);
CREATE INDEX IF NOT EXISTS idx_fs_tracking_events_waybill_time ON fs_tracking_events(waybill_no, event_time);
CREATE INDEX IF NOT EXISTS idx_fs_inventory_items_waybill ON fs_inventory_run_items(waybill_no);
CREATE INDEX IF NOT EXISTS idx_fs_dispatch_tasks_status_priority ON fs_dispatch_tasks(status, priority);

CREATE VIEW IF NOT EXISTS vw_current_inventory AS
    SELECT * FROM fs_waybills WHERE is_in_current_inventory = 1;
CREATE VIEW IF NOT EXISTS vw_new_today AS
    SELECT * FROM fs_waybills WHERE date(first_seen_at) = date('now', 'localtime');
CREATE VIEW IF NOT EXISTS vw_no_delivery_scan AS
    SELECT * FROM fs_waybills WHERE current_state IN ('NewArrival','PendingDeliveryScan');
CREATE VIEW IF NOT EXISTS vw_delivery_failed AS
    SELECT * FROM fs_waybills WHERE current_state = 'DeliveryFailed';
CREATE VIEW IF NOT EXISTS vw_waiting_return AS
    SELECT * FROM fs_waybills WHERE current_state = 'WaitingReturn';
CREATE VIEW IF NOT EXISTS vw_sla_overdue AS
    SELECT * FROM fs_waybills WHERE sla_status = 'OVERDUE';
CREATE VIEW IF NOT EXISTS vw_lost_risk AS
    SELECT * FROM fs_waybills WHERE current_state = 'LostRisk' OR risk_level = 'CRITICAL';
CREATE VIEW IF NOT EXISTS vw_stopped_1_day AS
    SELECT * FROM fs_waybills WHERE last_action_time IS NOT NULL AND julianday('now') - julianday(last_action_time) >= 1;
CREATE VIEW IF NOT EXISTS vw_stopped_3_days AS
    SELECT * FROM fs_waybills WHERE last_action_time IS NOT NULL AND julianday('now') - julianday(last_action_time) >= 3;
CREATE VIEW IF NOT EXISTS vw_stopped_7_days AS
    SELECT * FROM fs_waybills WHERE last_action_time IS NOT NULL AND julianday('now') - julianday(last_action_time) >= 7;
";

        public static readonly (string ColumnName, string Sql)[] WaybillColumnGuards =
        {
            ("is_checked", "ALTER TABLE fs_waybills ADD COLUMN is_checked INTEGER NOT NULL DEFAULT 0;"),
            ("checked_at", "ALTER TABLE fs_waybills ADD COLUMN checked_at TEXT NULL;"),
            ("checked_by", "ALTER TABLE fs_waybills ADD COLUMN checked_by TEXT NULL;"),
            ("is_enriched", "ALTER TABLE fs_waybills ADD COLUMN is_enriched INTEGER NOT NULL DEFAULT 0;"),
            ("enriched_at", "ALTER TABLE fs_waybills ADD COLUMN enriched_at TEXT NULL;")
        };

        public const string SchemaV1PostColumnIndexes = @"
CREATE INDEX IF NOT EXISTS idx_fs_waybills_checked ON fs_waybills(is_checked, checked_at);
CREATE INDEX IF NOT EXISTS idx_fs_waybills_enriched ON fs_waybills(is_enriched, enriched_at);
CREATE INDEX IF NOT EXISTS idx_fs_order_checks_waybill ON fs_order_checks(waybill_no, checked_at);
";

        // V2 — Hybrid local-first + Supabase sync (docs/hybrid-supabase-sync-plan.md).
        // fs_outbox: local writes queued for cloud push (offline-safe).
        public const string SchemaV2 = @"
CREATE TABLE IF NOT EXISTS fs_outbox (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    kind TEXT NOT NULL,
    ref_key TEXT NOT NULL,
    payload TEXT NOT NULL,
    created_at TEXT NOT NULL,
    attempts INTEGER NOT NULL DEFAULT 0,
    last_error TEXT,
    synced_at TEXT
);

CREATE INDEX IF NOT EXISTS idx_fs_outbox_pending ON fs_outbox(synced_at, id);
";

        // client_id: idempotency key ""<clientId>:<local id>"" so pushes/pulls never duplicate
        // rows across machines. origin: 'local' (this machine) or 'cloud' (merged from Supabase).
        public static readonly (string TableName, string ColumnName, string Sql)[] SyncColumnGuards =
        {
            ("fs_order_notes", "client_id", "ALTER TABLE fs_order_notes ADD COLUMN client_id TEXT NULL;"),
            ("fs_order_notes", "origin", "ALTER TABLE fs_order_notes ADD COLUMN origin TEXT NOT NULL DEFAULT 'local';"),
            ("fs_dispatch_tasks", "client_id", "ALTER TABLE fs_dispatch_tasks ADD COLUMN client_id TEXT NULL;"),
            ("fs_dispatch_tasks", "origin", "ALTER TABLE fs_dispatch_tasks ADD COLUMN origin TEXT NOT NULL DEFAULT 'local';")
        };

        public const string SchemaV2PostColumnIndexes = @"
CREATE UNIQUE INDEX IF NOT EXISTS idx_fs_order_notes_client_id ON fs_order_notes(client_id) WHERE client_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS idx_fs_dispatch_tasks_client_id ON fs_dispatch_tasks(client_id) WHERE client_id IS NOT NULL;
";
    }
}

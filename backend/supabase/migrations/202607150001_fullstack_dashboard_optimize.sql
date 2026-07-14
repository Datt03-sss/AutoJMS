-- FullStackOperation dashboard optimizations + security hardening.
-- Applied to project jrqxnviixmagiriqysov (autojms_database).
-- Safe to run more than once (idempotent).

-- 1. Hot-path partial indexes for FullStackOperation
--    tabDash: active waybills currently in inventory, newest first
create index if not exists idx_waybills_dash_hot
    on public.waybills(site_code, updated_at desc)
    where is_active and is_in_current_inventory;

--    tabThoiHieu (SLA monitor): active waybills by SLA status
create index if not exists idx_waybills_site_sla
    on public.waybills(site_code, sla_status)
    where is_active;

--    Risk alerts (30s alert timer): high-risk lookups
create index if not exists idx_waybills_site_risk
    on public.waybills(site_code, risk_level)
    where is_active and risk_score > 0;

--    Background tracking scheduler: due waybills only
create index if not exists idx_waybills_due_tracking
    on public.waybills(next_track_at)
    where is_active;

--    Open dispatch tasks by priority
create index if not exists idx_dispatch_tasks_open
    on public.dispatch_tasks(site_code, priority desc, created_at)
    where status = 'OPEN';

-- 2. Security hardening flagged patterns
--    Pin search_path on jwt_site_code (advisor: function_search_path_mutable)
create or replace function public.jwt_site_code()
returns text
language sql
stable
set search_path = public
as $$
    select nullif(coalesce(current_setting('request.jwt.claims', true), '')::jsonb ->> 'site_code', '');
$$;

grant execute on function public.jwt_site_code() to anon, authenticated;

--    Enable RLS on read-only config tables (advisor: rls_disabled_in_public)
alter table public.app_manifest enable row level security;
alter table public.app_modules enable row level security;
alter table public.app_configs enable row level security;

do $$
begin
    create policy app_manifest_public_read on public.app_manifest
        for select to anon, authenticated using (true);
exception when duplicate_object then null;
end $$;

do $$
begin
    create policy app_modules_public_read on public.app_modules
        for select to anon, authenticated using (true);
exception when duplicate_object then null;
end $$;

do $$
begin
    create policy app_configs_public_read on public.app_configs
        for select to anon, authenticated using (true);
exception when duplicate_object then null;
end $$;

-- 3. Keep planner stats fresh for the dashboard's paginated queries
analyze public.waybills, public.order_notes, public.order_checks, public.dispatch_tasks;

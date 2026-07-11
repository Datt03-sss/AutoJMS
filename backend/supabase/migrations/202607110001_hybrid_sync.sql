-- AutoJMS Hybrid Local-first + Supabase sync (docs/hybrid-supabase-sync-plan.md)
-- Project: bnsnnrlwfzxemmizknwy. Safe to run more than once (idempotent).
--
-- Adds:
--   * site_code tenancy on public.waybills + dashboard columns (Group A)
--   * workflow tables: order_notes (append-only), order_checks, dispatch_tasks (Group B)
--   * newest-wins merge RPCs + delta-pull RPCs (all SECURITY DEFINER; direct writes stay revoked)
--   * site-scoped inventory lease RPCs (reuses inventory_sync_leases, lease_name = 'inventory:'||site)
--   * RLS on all synced tables scoped by JWT claim site_code (anon key without claim keeps
--     current behavior; flip jwt_site_code() fallback in Phase 4 hardening)
--   * realtime publication for synced tables

create extension if not exists pgcrypto;

-- ---------------------------------------------------------------------------
-- 1. waybills: tenancy + dashboard (Group A) columns
-- ---------------------------------------------------------------------------
alter table public.waybills
    add column if not exists site_code text not null default '',
    add column if not exists is_in_current_inventory boolean not null default true,
    add column if not exists left_inventory_at text not null default '',
    add column if not exists first_seen_at text not null default '',
    add column if not exists last_seen_at text not null default '',
    add column if not exists current_state text not null default '',
    add column if not exists current_status text not null default '',
    add column if not exists last_action text not null default '',
    add column if not exists last_action_time text not null default '',
    add column if not exists last_site_code text not null default '',
    add column if not exists last_site_name text not null default '',
    add column if not exists employee_code text not null default '',
    add column if not exists employee_name text not null default '',
    add column if not exists receiver_name text not null default '',
    add column if not exists receiver_phone_masked text not null default '',
    add column if not exists age_hours double precision not null default 0,
    add column if not exists days_in_inventory double precision not null default 0,
    add column if not exists risk_score integer not null default 0,
    add column if not exists risk_level text not null default '',
    add column if not exists risk_reasons text not null default '',
    add column if not exists sla_status text not null default '',
    add column if not exists sla_deadline text not null default '',
    add column if not exists reback_status text not null default 'empty',
    add column if not exists in_hoan_scan_time text not null default 'empty';

create index if not exists idx_waybills_site_updated
    on public.waybills(site_code, updated_at desc);

create index if not exists idx_waybills_site_inventory
    on public.waybills(site_code, is_in_current_inventory);

-- ---------------------------------------------------------------------------
-- 2. Workflow tables (Group B)
-- ---------------------------------------------------------------------------
create table if not exists public.order_notes (
    id uuid primary key default gen_random_uuid(),
    site_code text not null,
    waybill_no text not null,
    note text not null,
    created_by text not null default '',
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    client_id text not null default '',            -- "<machine>:<local rowid>" for idempotent push
    unique (site_code, client_id)
);

create index if not exists idx_order_notes_site_updated
    on public.order_notes(site_code, updated_at desc);
create index if not exists idx_order_notes_waybill
    on public.order_notes(site_code, waybill_no);

create table if not exists public.order_checks (
    site_code text not null,
    waybill_no text not null,
    is_checked boolean not null default true,
    checked_at text not null default '',
    checked_by text not null default '',
    note text not null default '',
    updated_at timestamptz not null default now(),
    primary key (site_code, waybill_no)
);

create index if not exists idx_order_checks_site_updated
    on public.order_checks(site_code, updated_at desc);

create table if not exists public.dispatch_tasks (
    id uuid primary key default gen_random_uuid(),
    site_code text not null,
    waybill_no text not null,
    task_type text not null default 'CHECK_PHYSICAL_STOCK',
    priority integer not null default 0,
    status text not null default 'OPEN',
    assigned_to text not null default '',
    due_at text not null default '',
    created_at timestamptz not null default now(),
    completed_at timestamptz,
    updated_at timestamptz not null default now(),
    client_id text not null default '',
    unique (site_code, client_id)
);

create index if not exists idx_dispatch_tasks_site_updated
    on public.dispatch_tasks(site_code, updated_at desc);
create index if not exists idx_dispatch_tasks_waybill
    on public.dispatch_tasks(site_code, waybill_no);

-- ---------------------------------------------------------------------------
-- 3. RLS scoped by JWT claim site_code
--    jwt_site_code() returns NULL for plain anon key (no claim) -> policy passes,
--    preserving current behavior. Phase 4 hardening: license server issues JWTs
--    with site_code claim, then replace "jwt_site_code() is null" with "false".
-- ---------------------------------------------------------------------------
create or replace function public.jwt_site_code()
returns text
language sql
stable
as $$
    select nullif(coalesce(current_setting('request.jwt.claims', true), '')::jsonb ->> 'site_code', '');
$$;

alter table public.waybills enable row level security;
alter table public.order_notes enable row level security;
alter table public.order_checks enable row level security;
alter table public.dispatch_tasks enable row level security;
alter table public.inventory_sync_leases enable row level security;

do $$
begin
    create policy waybills_site_read on public.waybills
        for select to anon, authenticated
        using (public.jwt_site_code() is null or site_code = public.jwt_site_code());
exception when duplicate_object then null;
end $$;

do $$
begin
    create policy order_notes_site_read on public.order_notes
        for select to anon, authenticated
        using (public.jwt_site_code() is null or site_code = public.jwt_site_code());
exception when duplicate_object then null;
end $$;

do $$
begin
    create policy order_checks_site_read on public.order_checks
        for select to anon, authenticated
        using (public.jwt_site_code() is null or site_code = public.jwt_site_code());
exception when duplicate_object then null;
end $$;

do $$
begin
    create policy dispatch_tasks_site_read on public.dispatch_tasks
        for select to anon, authenticated
        using (public.jwt_site_code() is null or site_code = public.jwt_site_code());
exception when duplicate_object then null;
end $$;

do $$
begin
    create policy leases_read on public.inventory_sync_leases
        for select to anon, authenticated
        using (true);
exception when duplicate_object then null;
end $$;

-- Direct table writes stay revoked (202606110002); all writes go through the
-- SECURITY DEFINER RPCs below. Ensure select is granted for delta-pull/realtime.
revoke insert, update, delete on public.waybills from anon, authenticated;
revoke insert, update, delete on public.inventory_sync_leases from anon, authenticated;
grant select on public.waybills, public.order_notes, public.order_checks,
               public.dispatch_tasks, public.inventory_sync_leases to anon, authenticated;

-- ---------------------------------------------------------------------------
-- 4. Site-scoped lease RPCs (lease_name = 'inventory:'||site_code)
-- ---------------------------------------------------------------------------
create or replace function public.try_acquire_site_lease(
    p_site_code text,
    p_owner_id text,
    p_lease_seconds integer default 1800
)
returns boolean
language plpgsql
security definer
set search_path = public
as $$
declare
    v_now timestamptz := now();
    v_until timestamptz := now() + make_interval(secs => greatest(coalesce(p_lease_seconds, 1800), 1));
    v_name text := 'inventory:' || coalesce(nullif(trim(p_site_code), ''), 'default');
begin
    if public.jwt_site_code() is not null and public.jwt_site_code() <> trim(p_site_code) then
        return false;
    end if;

    insert into public.inventory_sync_leases (lease_name, owner_id, leased_until, updated_at)
    values (v_name, p_owner_id, v_until, v_now)
    on conflict (lease_name) do update set
        owner_id = excluded.owner_id,
        leased_until = excluded.leased_until,
        updated_at = excluded.updated_at
    where public.inventory_sync_leases.leased_until < v_now
       or public.inventory_sync_leases.owner_id = p_owner_id;

    return exists (
        select 1 from public.inventory_sync_leases
        where lease_name = v_name
          and owner_id = p_owner_id
          and leased_until >= v_now
    );
end;
$$;

create or replace function public.refresh_site_lease(
    p_site_code text,
    p_owner_id text,
    p_lease_seconds integer default 1800
)
returns boolean
language plpgsql
security definer
set search_path = public
as $$
declare
    v_name text := 'inventory:' || coalesce(nullif(trim(p_site_code), ''), 'default');
begin
    update public.inventory_sync_leases
    set leased_until = now() + make_interval(secs => greatest(coalesce(p_lease_seconds, 1800), 1)),
        updated_at = now()
    where lease_name = v_name
      and owner_id = p_owner_id
      and leased_until >= now();
    return found;
end;
$$;

create or replace function public.release_site_lease(
    p_site_code text,
    p_owner_id text
)
returns boolean
language plpgsql
security definer
set search_path = public
as $$
declare
    v_name text := 'inventory:' || coalesce(nullif(trim(p_site_code), ''), 'default');
begin
    delete from public.inventory_sync_leases
    where lease_name = v_name
      and owner_id = p_owner_id;
    return found;
end;
$$;

-- ---------------------------------------------------------------------------
-- 5. Group A: newest-wins waybill merge + delta pull
-- ---------------------------------------------------------------------------
create or replace function public.merge_waybill_rows_v2(
    p_site_code text,
    p_rows jsonb
)
returns integer
language plpgsql
security definer
set search_path = public
as $$
declare
    v_count integer := 0;
    v_site text := coalesce(nullif(trim(p_site_code), ''), 'default');
begin
    if public.jwt_site_code() is not null and public.jwt_site_code() <> v_site then
        return 0;
    end if;

    with rows as (
        select * from jsonb_to_recordset(coalesce(p_rows, '[]'::jsonb)) as r(
            waybill_no text,
            is_in_current_inventory boolean,
            left_inventory_at text,
            first_seen_at text,
            last_seen_at text,
            current_state text,
            current_status text,
            last_action text,
            last_action_time text,
            last_site_code text,
            last_site_name text,
            employee_code text,
            employee_name text,
            receiver_name text,
            receiver_phone_masked text,
            age_hours double precision,
            days_in_inventory double precision,
            risk_score integer,
            risk_level text,
            risk_reasons text,
            sla_status text,
            sla_deadline text,
            trang_thai_hien_tai text,
            thao_tac_cuoi text,
            thoi_gian_thao_tac text,
            thoi_gian_yeu_cau_phat_lai text,
            nhan_vien_kien_van_de text,
            nguyen_nhan_kien_van_de text,
            buu_cuc_thao_tac text,
            nguoi_thao_tac text,
            dau_chuyen_hoan text,
            dia_chi_nhan_hang text,
            phuong text,
            noi_dung_hang_hoa text,
            cod_thuc_te text,
            pttt text,
            nhan_vien_nhan_hang text,
            dia_chi_lay_hang text,
            thoi_gian_nhan_hang text,
            ten_nguoi_gui text,
            trong_luong text,
            ma_doan_full text,
            ma_doan_1 text,
            ma_doan_2 text,
            ma_doan_3 text,
            reback_status text,
            in_hoan_scan_time text,
            print_count integer,
            updated_at timestamptz
        )
    ),
    upserted as (
        insert into public.waybills (
            waybill_no, site_code,
            is_in_current_inventory, left_inventory_at, first_seen_at, last_seen_at,
            current_state, current_status, last_action, last_action_time,
            last_site_code, last_site_name, employee_code, employee_name,
            receiver_name, receiver_phone_masked, age_hours, days_in_inventory,
            risk_score, risk_level, risk_reasons, sla_status, sla_deadline,
            trang_thai_hien_tai, thao_tac_cuoi, thoi_gian_thao_tac,
            thoi_gian_yeu_cau_phat_lai, nhan_vien_kien_van_de, nguyen_nhan_kien_van_de,
            buu_cuc_thao_tac, nguoi_thao_tac, dau_chuyen_hoan, dia_chi_nhan_hang,
            phuong, noi_dung_hang_hoa, cod_thuc_te, pttt, nhan_vien_nhan_hang,
            dia_chi_lay_hang, thoi_gian_nhan_hang, ten_nguoi_gui, trong_luong,
            ma_doan_full, ma_doan_1, ma_doan_2, ma_doan_3,
            reback_status, in_hoan_scan_time, print_count, updated_at
        )
        select
            trim(waybill_no), v_site,
            coalesce(is_in_current_inventory, true),
            coalesce(left_inventory_at, ''), coalesce(first_seen_at, ''), coalesce(last_seen_at, ''),
            coalesce(current_state, ''), coalesce(current_status, ''),
            coalesce(last_action, ''), coalesce(last_action_time, ''),
            coalesce(last_site_code, ''), coalesce(last_site_name, ''),
            coalesce(employee_code, ''), coalesce(employee_name, ''),
            coalesce(receiver_name, ''), coalesce(receiver_phone_masked, ''),
            coalesce(age_hours, 0), coalesce(days_in_inventory, 0),
            coalesce(risk_score, 0), coalesce(risk_level, ''), coalesce(risk_reasons, ''),
            coalesce(sla_status, ''), coalesce(sla_deadline, ''),
            coalesce(nullif(trang_thai_hien_tai, ''), 'empty'),
            coalesce(nullif(thao_tac_cuoi, ''), 'empty'),
            coalesce(nullif(thoi_gian_thao_tac, ''), 'empty'),
            coalesce(nullif(thoi_gian_yeu_cau_phat_lai, ''), 'empty'),
            coalesce(nullif(nhan_vien_kien_van_de, ''), 'empty'),
            coalesce(nullif(nguyen_nhan_kien_van_de, ''), 'empty'),
            coalesce(nullif(buu_cuc_thao_tac, ''), 'empty'),
            coalesce(nullif(nguoi_thao_tac, ''), 'empty'),
            coalesce(nullif(dau_chuyen_hoan, ''), 'empty'),
            coalesce(nullif(dia_chi_nhan_hang, ''), 'empty'),
            coalesce(nullif(phuong, ''), 'empty'),
            coalesce(nullif(noi_dung_hang_hoa, ''), 'empty'),
            coalesce(nullif(cod_thuc_te, ''), 'empty'),
            coalesce(nullif(pttt, ''), 'empty'),
            coalesce(nullif(nhan_vien_nhan_hang, ''), 'empty'),
            coalesce(nullif(dia_chi_lay_hang, ''), 'empty'),
            coalesce(nullif(thoi_gian_nhan_hang, ''), 'empty'),
            coalesce(nullif(ten_nguoi_gui, ''), 'empty'),
            coalesce(nullif(trong_luong, ''), 'empty'),
            coalesce(nullif(ma_doan_full, ''), 'empty'),
            coalesce(nullif(ma_doan_1, ''), 'empty'),
            coalesce(nullif(ma_doan_2, ''), 'empty'),
            coalesce(nullif(ma_doan_3, ''), 'empty'),
            coalesce(nullif(reback_status, ''), 'empty'),
            coalesce(nullif(in_hoan_scan_time, ''), 'empty'),
            coalesce(print_count, 0),
            coalesce(updated_at, now())
        from rows
        where nullif(trim(waybill_no), '') is not null
        on conflict (waybill_no) do update set
            site_code = excluded.site_code,
            is_in_current_inventory = excluded.is_in_current_inventory,
            left_inventory_at = excluded.left_inventory_at,
            first_seen_at = excluded.first_seen_at,
            last_seen_at = excluded.last_seen_at,
            current_state = excluded.current_state,
            current_status = excluded.current_status,
            last_action = excluded.last_action,
            last_action_time = excluded.last_action_time,
            last_site_code = excluded.last_site_code,
            last_site_name = excluded.last_site_name,
            employee_code = excluded.employee_code,
            employee_name = excluded.employee_name,
            receiver_name = excluded.receiver_name,
            receiver_phone_masked = excluded.receiver_phone_masked,
            age_hours = excluded.age_hours,
            days_in_inventory = excluded.days_in_inventory,
            risk_score = excluded.risk_score,
            risk_level = excluded.risk_level,
            risk_reasons = excluded.risk_reasons,
            sla_status = excluded.sla_status,
            sla_deadline = excluded.sla_deadline,
            trang_thai_hien_tai = excluded.trang_thai_hien_tai,
            thao_tac_cuoi = excluded.thao_tac_cuoi,
            thoi_gian_thao_tac = excluded.thoi_gian_thao_tac,
            thoi_gian_yeu_cau_phat_lai = excluded.thoi_gian_yeu_cau_phat_lai,
            nhan_vien_kien_van_de = excluded.nhan_vien_kien_van_de,
            nguyen_nhan_kien_van_de = excluded.nguyen_nhan_kien_van_de,
            buu_cuc_thao_tac = excluded.buu_cuc_thao_tac,
            nguoi_thao_tac = excluded.nguoi_thao_tac,
            dau_chuyen_hoan = excluded.dau_chuyen_hoan,
            dia_chi_nhan_hang = excluded.dia_chi_nhan_hang,
            phuong = excluded.phuong,
            noi_dung_hang_hoa = excluded.noi_dung_hang_hoa,
            cod_thuc_te = excluded.cod_thuc_te,
            pttt = excluded.pttt,
            nhan_vien_nhan_hang = excluded.nhan_vien_nhan_hang,
            dia_chi_lay_hang = excluded.dia_chi_lay_hang,
            thoi_gian_nhan_hang = excluded.thoi_gian_nhan_hang,
            ten_nguoi_gui = excluded.ten_nguoi_gui,
            trong_luong = excluded.trong_luong,
            ma_doan_full = excluded.ma_doan_full,
            ma_doan_1 = excluded.ma_doan_1,
            ma_doan_2 = excluded.ma_doan_2,
            ma_doan_3 = excluded.ma_doan_3,
            reback_status = excluded.reback_status,
            in_hoan_scan_time = excluded.in_hoan_scan_time,
            print_count = excluded.print_count,
            updated_at = excluded.updated_at
        where excluded.updated_at >= public.waybills.updated_at   -- newest-wins
        returning 1
    )
    select count(*) into v_count from upserted;
    return coalesce(v_count, 0);
end;
$$;

create or replace function public.pull_waybill_delta(
    p_site_code text,
    p_since timestamptz,
    p_limit integer default 1000
)
returns setof public.waybills
language sql
security definer
set search_path = public
stable
as $$
    select *
    from public.waybills
    where site_code = coalesce(nullif(trim(p_site_code), ''), 'default')
      and (public.jwt_site_code() is null or site_code = public.jwt_site_code())
      and updated_at > coalesce(p_since, '1970-01-01'::timestamptz)
    order by updated_at asc
    limit least(greatest(coalesce(p_limit, 1000), 1), 5000);
$$;

-- ---------------------------------------------------------------------------
-- 6. Group B RPCs: notes (append-only), checks (newest-wins), tasks (newest-wins)
-- ---------------------------------------------------------------------------
create or replace function public.push_order_notes(
    p_site_code text,
    p_rows jsonb
)
returns integer
language plpgsql
security definer
set search_path = public
as $$
declare
    v_count integer := 0;
    v_site text := coalesce(nullif(trim(p_site_code), ''), 'default');
begin
    if public.jwt_site_code() is not null and public.jwt_site_code() <> v_site then
        return 0;
    end if;

    with rows as (
        select * from jsonb_to_recordset(coalesce(p_rows, '[]'::jsonb)) as r(
            waybill_no text, note text, created_by text,
            created_at timestamptz, client_id text
        )
    ),
    inserted as (
        insert into public.order_notes (site_code, waybill_no, note, created_by, created_at, updated_at, client_id)
        select v_site, trim(waybill_no), note, coalesce(created_by, ''),
               coalesce(created_at, now()), now(), coalesce(client_id, gen_random_uuid()::text)
        from rows
        where nullif(trim(waybill_no), '') is not null and note is not null
        on conflict (site_code, client_id) do nothing
        returning 1
    )
    select count(*) into v_count from inserted;
    return coalesce(v_count, 0);
end;
$$;

create or replace function public.pull_order_notes(
    p_site_code text,
    p_since timestamptz,
    p_limit integer default 1000
)
returns setof public.order_notes
language sql
security definer
set search_path = public
stable
as $$
    select *
    from public.order_notes
    where site_code = coalesce(nullif(trim(p_site_code), ''), 'default')
      and (public.jwt_site_code() is null or site_code = public.jwt_site_code())
      and updated_at > coalesce(p_since, '1970-01-01'::timestamptz)
    order by updated_at asc
    limit least(greatest(coalesce(p_limit, 1000), 1), 5000);
$$;

create or replace function public.merge_order_checks(
    p_site_code text,
    p_rows jsonb
)
returns integer
language plpgsql
security definer
set search_path = public
as $$
declare
    v_count integer := 0;
    v_site text := coalesce(nullif(trim(p_site_code), ''), 'default');
begin
    if public.jwt_site_code() is not null and public.jwt_site_code() <> v_site then
        return 0;
    end if;

    with rows as (
        select * from jsonb_to_recordset(coalesce(p_rows, '[]'::jsonb)) as r(
            waybill_no text, is_checked boolean, checked_at text,
            checked_by text, note text, updated_at timestamptz
        )
    ),
    upserted as (
        insert into public.order_checks (site_code, waybill_no, is_checked, checked_at, checked_by, note, updated_at)
        select v_site, trim(waybill_no), coalesce(is_checked, true),
               coalesce(checked_at, ''), coalesce(checked_by, ''), coalesce(note, ''),
               coalesce(updated_at, now())
        from rows
        where nullif(trim(waybill_no), '') is not null
        on conflict (site_code, waybill_no) do update set
            is_checked = excluded.is_checked,
            checked_at = excluded.checked_at,
            checked_by = excluded.checked_by,
            note = excluded.note,
            updated_at = excluded.updated_at
        where excluded.updated_at >= public.order_checks.updated_at
        returning 1
    )
    select count(*) into v_count from upserted;
    return coalesce(v_count, 0);
end;
$$;

create or replace function public.pull_order_checks(
    p_site_code text,
    p_since timestamptz,
    p_limit integer default 1000
)
returns setof public.order_checks
language sql
security definer
set search_path = public
stable
as $$
    select *
    from public.order_checks
    where site_code = coalesce(nullif(trim(p_site_code), ''), 'default')
      and (public.jwt_site_code() is null or site_code = public.jwt_site_code())
      and updated_at > coalesce(p_since, '1970-01-01'::timestamptz)
    order by updated_at asc
    limit least(greatest(coalesce(p_limit, 1000), 1), 5000);
$$;

create or replace function public.merge_dispatch_tasks(
    p_site_code text,
    p_rows jsonb
)
returns integer
language plpgsql
security definer
set search_path = public
as $$
declare
    v_count integer := 0;
    v_site text := coalesce(nullif(trim(p_site_code), ''), 'default');
begin
    if public.jwt_site_code() is not null and public.jwt_site_code() <> v_site then
        return 0;
    end if;

    with rows as (
        select * from jsonb_to_recordset(coalesce(p_rows, '[]'::jsonb)) as r(
            waybill_no text, task_type text, priority integer, status text,
            assigned_to text, due_at text, created_at timestamptz,
            completed_at timestamptz, updated_at timestamptz, client_id text
        )
    ),
    upserted as (
        insert into public.dispatch_tasks (
            site_code, waybill_no, task_type, priority, status,
            assigned_to, due_at, created_at, completed_at, updated_at, client_id)
        select v_site, trim(waybill_no),
               coalesce(nullif(task_type, ''), 'CHECK_PHYSICAL_STOCK'),
               coalesce(priority, 0),
               coalesce(nullif(status, ''), 'OPEN'),
               coalesce(assigned_to, ''), coalesce(due_at, ''),
               coalesce(created_at, now()), completed_at,
               coalesce(updated_at, now()),
               coalesce(client_id, gen_random_uuid()::text)
        from rows
        where nullif(trim(waybill_no), '') is not null
        on conflict (site_code, client_id) do update set
            task_type = excluded.task_type,
            priority = excluded.priority,
            status = excluded.status,
            assigned_to = excluded.assigned_to,
            due_at = excluded.due_at,
            completed_at = excluded.completed_at,
            updated_at = excluded.updated_at
        where excluded.updated_at >= public.dispatch_tasks.updated_at
        returning 1
    )
    select count(*) into v_count from upserted;
    return coalesce(v_count, 0);
end;
$$;

create or replace function public.pull_dispatch_tasks(
    p_site_code text,
    p_since timestamptz,
    p_limit integer default 1000
)
returns setof public.dispatch_tasks
language sql
security definer
set search_path = public
stable
as $$
    select *
    from public.dispatch_tasks
    where site_code = coalesce(nullif(trim(p_site_code), ''), 'default')
      and (public.jwt_site_code() is null or site_code = public.jwt_site_code())
      and updated_at > coalesce(p_since, '1970-01-01'::timestamptz)
    order by updated_at asc
    limit least(greatest(coalesce(p_limit, 1000), 1), 5000);
$$;

-- ---------------------------------------------------------------------------
-- 7. Grants for new RPCs
-- ---------------------------------------------------------------------------
grant execute on function public.try_acquire_site_lease(text, text, integer) to anon, authenticated;
grant execute on function public.refresh_site_lease(text, text, integer) to anon, authenticated;
grant execute on function public.release_site_lease(text, text) to anon, authenticated;
grant execute on function public.merge_waybill_rows_v2(text, jsonb) to anon, authenticated;
grant execute on function public.pull_waybill_delta(text, timestamptz, integer) to anon, authenticated;
grant execute on function public.push_order_notes(text, jsonb) to anon, authenticated;
grant execute on function public.pull_order_notes(text, timestamptz, integer) to anon, authenticated;
grant execute on function public.merge_order_checks(text, jsonb) to anon, authenticated;
grant execute on function public.pull_order_checks(text, timestamptz, integer) to anon, authenticated;
grant execute on function public.merge_dispatch_tasks(text, jsonb) to anon, authenticated;
grant execute on function public.pull_dispatch_tasks(text, timestamptz, integer) to anon, authenticated;
grant execute on function public.jwt_site_code() to anon, authenticated;

-- ---------------------------------------------------------------------------
-- 8. Realtime publication
-- ---------------------------------------------------------------------------
do $$
begin
    if not exists (
        select 1 from pg_publication_tables
        where pubname = 'supabase_realtime' and schemaname = 'public' and tablename = 'waybills'
    ) then
        alter publication supabase_realtime add table public.waybills;
    end if;
    if not exists (
        select 1 from pg_publication_tables
        where pubname = 'supabase_realtime' and schemaname = 'public' and tablename = 'order_notes'
    ) then
        alter publication supabase_realtime add table public.order_notes;
    end if;
    if not exists (
        select 1 from pg_publication_tables
        where pubname = 'supabase_realtime' and schemaname = 'public' and tablename = 'order_checks'
    ) then
        alter publication supabase_realtime add table public.order_checks;
    end if;
    if not exists (
        select 1 from pg_publication_tables
        where pubname = 'supabase_realtime' and schemaname = 'public' and tablename = 'dispatch_tasks'
    ) then
        alter publication supabase_realtime add table public.dispatch_tasks;
    end if;
end $$;

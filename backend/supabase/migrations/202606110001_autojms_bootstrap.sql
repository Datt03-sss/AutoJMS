-- AutoJMS Supabase bootstrap for project bnsnnrlwfzxemmizknwy.
-- Safe to run more than once. Keep Velopack binaries on GitHub Releases;
-- Supabase Storage is only for small manifest/config JSON files.

create extension if not exists pgcrypto;

insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values (
    'autojms-modules',
    'autojms-modules',
    true,
    52428800,
    array['application/json', 'text/plain', 'application/octet-stream']::text[]
)
on conflict (id) do update set
    public = excluded.public,
    file_size_limit = excluded.file_size_limit,
    allowed_mime_types = excluded.allowed_mime_types;

do $$
begin
    create policy "autojms_modules_public_read"
        on storage.objects
        for select
        to anon, authenticated
        using (bucket_id = 'autojms-modules');
exception
    when duplicate_object then null;
end $$;

create table if not exists public.app_manifest (
    id uuid primary key default gen_random_uuid(),
    manifest_version text not null default '2026.05.25',
    min_core_version text not null default '1.26.05.0',
    created_at timestamptz not null default now()
);

create table if not exists public.app_modules (
    id uuid primary key default gen_random_uuid(),
    name text not null,
    version text not null,
    file text not null,
    sha256 text not null default '',
    signature text,
    firebase_url text not null default '',
    requires jsonb not null default '[]'::jsonb,
    required boolean not null default false,
    enabled boolean not null default true,
    created_at timestamptz not null default now(),
    unique(name)
);

create table if not exists public.app_configs (
    key text primary key,
    value jsonb not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

insert into public.app_manifest (manifest_version, min_core_version)
values ('2026.05.25', '1.26.05.0')
on conflict do nothing;

insert into public.app_configs (key, value) values
    ('timings', '{"retryDelay":800,"switchDelay":1200}'::jsonb),
    ('features', '{"FAST_MODE":true,"SMART_RETRY":false}'::jsonb)
on conflict (key) do nothing;

create index if not exists idx_modules_name on public.app_modules(name);
create index if not exists idx_modules_enabled on public.app_modules(enabled);

create table if not exists public.waybills (
    waybill_no text primary key,
    trang_thai_hien_tai text not null default 'empty',
    thao_tac_cuoi text not null default 'empty',
    thoi_gian_thao_tac text not null default 'empty',
    thoi_gian_yeu_cau_phat_lai text not null default 'empty',
    nhan_vien_kien_van_de text not null default 'empty',
    nguyen_nhan_kien_van_de text not null default 'empty',
    buu_cuc_thao_tac text not null default 'empty',
    nguoi_thao_tac text not null default 'empty',
    dau_chuyen_hoan text not null default 'empty',
    dia_chi_nhan_hang text not null default 'empty',
    phuong text not null default 'empty',
    noi_dung_hang_hoa text not null default 'empty',
    cod_thuc_te text not null default 'empty',
    pttt text not null default 'empty',
    nhan_vien_nhan_hang text not null default 'empty',
    dia_chi_lay_hang text not null default 'empty',
    thoi_gian_nhan_hang text not null default 'empty',
    ten_nguoi_gui text not null default 'empty',
    trong_luong text not null default 'empty',
    ma_doan_full text not null default 'empty',
    ma_doan_1 text not null default 'empty',
    ma_doan_2 text not null default 'empty',
    ma_doan_3 text not null default 'empty',
    reback_status text not null default 'empty',
    in_hoan_scan_time text not null default 'empty',
    print_count integer not null default 0,
    is_active boolean not null default true,
    tracking_interval_mins integer not null default 30,
    last_tracked_at timestamptz not null default '1970-01-01 00:00:00+00',
    next_track_at timestamptz not null default now(),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create index if not exists idx_waybills_active_next_track
    on public.waybills(is_active, next_track_at);

create index if not exists idx_waybills_updated_at
    on public.waybills(updated_at desc);

create table if not exists public.inventory_sync_leases (
    lease_name text primary key,
    owner_id text not null,
    leased_until timestamptz not null,
    completed_at timestamptz,
    updated_at timestamptz not null default now()
);

create or replace function public.try_acquire_inventory_lease(
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
begin
    insert into public.inventory_sync_leases (lease_name, owner_id, leased_until, updated_at)
    values ('inventory', p_owner_id, v_until, v_now)
    on conflict (lease_name) do update set
        owner_id = excluded.owner_id,
        leased_until = excluded.leased_until,
        updated_at = excluded.updated_at
    where public.inventory_sync_leases.leased_until < v_now
       or public.inventory_sync_leases.owner_id = p_owner_id;

    return exists (
        select 1
        from public.inventory_sync_leases
        where lease_name = 'inventory'
          and owner_id = p_owner_id
          and leased_until >= v_now
    );
end;
$$;

create or replace function public.refresh_inventory_lease(
    p_owner_id text,
    p_lease_seconds integer default 1800
)
returns boolean
language plpgsql
security definer
set search_path = public
as $$
begin
    update public.inventory_sync_leases
    set leased_until = now() + make_interval(secs => greatest(coalesce(p_lease_seconds, 1800), 1)),
        updated_at = now()
    where lease_name = 'inventory'
      and owner_id = p_owner_id
      and leased_until >= now();

    return found;
end;
$$;

create or replace function public.release_inventory_lease(p_owner_id text)
returns boolean
language plpgsql
security definer
set search_path = public
as $$
begin
    delete from public.inventory_sync_leases
    where lease_name = 'inventory'
      and owner_id = p_owner_id;

    return found;
end;
$$;

create or replace function public.complete_inventory_sync(p_owner_id text)
returns boolean
language plpgsql
security definer
set search_path = public
as $$
begin
    update public.inventory_sync_leases
    set completed_at = now(),
        updated_at = now()
    where lease_name = 'inventory'
      and owner_id = p_owner_id;

    return found;
end;
$$;

create or replace function public.upsert_new_waybills(p_waybills text[])
returns integer
language plpgsql
security definer
set search_path = public
as $$
declare
    v_count integer := 0;
begin
    with cleaned as (
        select distinct nullif(trim(x), '') as waybill_no
        from unnest(coalesce(p_waybills, array[]::text[])) as x
    ),
    inserted as (
        insert into public.waybills (waybill_no, next_track_at, updated_at)
        select waybill_no, now(), now()
        from cleaned
        where waybill_no is not null
        on conflict (waybill_no) do nothing
        returning 1
    )
    select count(*) into v_count from inserted;

    return coalesce(v_count, 0);
end;
$$;

create or replace function public.merge_waybill_tracking_rows(p_rows jsonb)
returns integer
language plpgsql
security definer
set search_path = public
as $$
declare
    v_count integer := 0;
begin
    with rows as (
        select *
        from jsonb_to_recordset(coalesce(p_rows, '[]'::jsonb)) as r(
            waybill_no text,
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
            print_count integer,
            is_active boolean,
            tracking_interval_mins integer,
            last_tracked_at timestamptz,
            next_track_at timestamptz,
            updated_at timestamptz
        )
    ),
    upserted as (
        insert into public.waybills (
            waybill_no,
            trang_thai_hien_tai,
            thao_tac_cuoi,
            thoi_gian_thao_tac,
            thoi_gian_yeu_cau_phat_lai,
            nhan_vien_kien_van_de,
            nguyen_nhan_kien_van_de,
            buu_cuc_thao_tac,
            nguoi_thao_tac,
            dau_chuyen_hoan,
            dia_chi_nhan_hang,
            phuong,
            noi_dung_hang_hoa,
            cod_thuc_te,
            pttt,
            nhan_vien_nhan_hang,
            dia_chi_lay_hang,
            thoi_gian_nhan_hang,
            ten_nguoi_gui,
            trong_luong,
            ma_doan_full,
            ma_doan_1,
            ma_doan_2,
            ma_doan_3,
            print_count,
            is_active,
            tracking_interval_mins,
            last_tracked_at,
            next_track_at,
            updated_at
        )
        select
            trim(waybill_no),
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
            coalesce(print_count, 0),
            coalesce(is_active, true),
            coalesce(tracking_interval_mins, 30),
            coalesce(last_tracked_at, now()),
            coalesce(next_track_at, now() + make_interval(mins => coalesce(tracking_interval_mins, 30))),
            coalesce(updated_at, now())
        from rows
        where nullif(trim(waybill_no), '') is not null
        on conflict (waybill_no) do update set
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
            print_count = excluded.print_count,
            is_active = excluded.is_active,
            tracking_interval_mins = excluded.tracking_interval_mins,
            last_tracked_at = excluded.last_tracked_at,
            next_track_at = excluded.next_track_at,
            updated_at = excluded.updated_at
        returning 1
    )
    select count(*) into v_count from upserted;

    return coalesce(v_count, 0);
end;
$$;

grant usage on schema public to anon, authenticated;
grant select, insert, update on public.waybills to anon, authenticated;
grant select, insert, update, delete on public.inventory_sync_leases to anon, authenticated;
grant select on public.app_manifest, public.app_modules, public.app_configs to anon, authenticated;
grant execute on function public.try_acquire_inventory_lease(text, integer) to anon, authenticated;
grant execute on function public.refresh_inventory_lease(text, integer) to anon, authenticated;
grant execute on function public.release_inventory_lease(text) to anon, authenticated;
grant execute on function public.complete_inventory_sync(text) to anon, authenticated;
grant execute on function public.upsert_new_waybills(text[]) to anon, authenticated;
grant execute on function public.merge_waybill_tracking_rows(jsonb) to anon, authenticated;

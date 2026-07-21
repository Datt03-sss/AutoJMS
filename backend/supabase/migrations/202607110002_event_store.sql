-- AutoJMS Event Store (docs/roadmap: Event Pipeline + ODS, Phase 2).
-- Remote append-only event log shared across machines of a site.
-- Project: bnsnnrlwfzxemmizknwy. Idempotent.
--
-- Design:
--   * fingerprint UNIQUE per site = cross-machine dedupe (same observation once).
--   * seq bigserial = server-assigned monotonic order -> robust delta cursor that
--     does not depend on client clocks (fixes multi-machine clock skew).
--   * append/dedupe only; never overwrites. Projection stays in public.waybills.
--   * RLS scoped by site_code via jwt_site_code() (added in 202607110001).

create extension if not exists pgcrypto;

create table if not exists public.waybill_events (
    seq bigint generated always as identity primary key,
    event_id text not null,
    site_code text not null,
    waybill_no text not null,
    event_type text not null,
    event_time timestamptz not null,
    source text not null default '',
    source_client text not null default '',
    fingerprint text not null,
    payload jsonb not null default '{}'::jsonb,
    observed_at timestamptz not null default now(),
    schema_version integer not null default 1,
    created_at timestamptz not null default now(),
    unique (site_code, fingerprint)
);

create index if not exists idx_waybill_events_site_seq
    on public.waybill_events(site_code, seq);
create index if not exists idx_waybill_events_site_waybill
    on public.waybill_events(site_code, waybill_no, event_time);

alter table public.waybill_events enable row level security;

do $$
begin
    create policy waybill_events_site_read on public.waybill_events
        for select to anon, authenticated
        using (public.jwt_site_code() is null or site_code = public.jwt_site_code());
exception when duplicate_object then null;
end $$;

-- Writes go through the SECURITY DEFINER RPC only.
revoke insert, update, delete on public.waybill_events from anon, authenticated;
grant select on public.waybill_events to anon, authenticated;

-- Append a batch of events; dedupe on (site_code, fingerprint). Returns count inserted.
create or replace function public.append_waybill_events(
    p_site_code text,
    p_events jsonb
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
        select * from jsonb_to_recordset(coalesce(p_events, '[]'::jsonb)) as r(
            event_id text,
            waybill_no text,
            event_type text,
            event_time timestamptz,
            source text,
            source_client text,
            fingerprint text,
            payload jsonb,
            observed_at timestamptz,
            schema_version integer
        )
    ),
    inserted as (
        insert into public.waybill_events (
            event_id, site_code, waybill_no, event_type, event_time,
            source, source_client, fingerprint, payload, observed_at, schema_version)
        select
            coalesce(nullif(event_id, ''), gen_random_uuid()::text),
            v_site,
            upper(trim(waybill_no)),
            event_type,
            coalesce(event_time, now()),
            coalesce(source, ''),
            coalesce(source_client, ''),
            fingerprint,
            coalesce(payload, '{}'::jsonb),
            coalesce(observed_at, now()),
            coalesce(schema_version, 1)
        from rows
        where nullif(trim(waybill_no), '') is not null
          and nullif(trim(fingerprint), '') is not null
        on conflict (site_code, fingerprint) do nothing
        returning 1
    )
    select count(*) into v_count from inserted;
    return coalesce(v_count, 0);
end;
$$;

-- Delta pull by server seq cursor (monotonic, clock-independent).
create or replace function public.pull_events_delta(
    p_site_code text,
    p_since_seq bigint,
    p_limit integer default 2000
)
returns setof public.waybill_events
language sql
security definer
set search_path = public
stable
as $$
    select *
    from public.waybill_events
    where site_code = coalesce(nullif(trim(p_site_code), ''), 'default')
      and (public.jwt_site_code() is null or site_code = public.jwt_site_code())
      and seq > coalesce(p_since_seq, 0)
    order by seq asc
    limit least(greatest(coalesce(p_limit, 2000), 1), 10000);
$$;

grant execute on function public.append_waybill_events(text, jsonb) to anon, authenticated;
grant execute on function public.pull_events_delta(text, bigint, integer) to anon, authenticated;

-- Realtime doorbell for the event stream.
do $$
begin
    if not exists (
        select 1 from pg_publication_tables
        where pubname = 'supabase_realtime' and schemaname = 'public' and tablename = 'waybill_events'
    ) then
        alter publication supabase_realtime add table public.waybill_events;
    end if;
end $$;

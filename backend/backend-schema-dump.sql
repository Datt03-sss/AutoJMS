--
-- AutoJMS DataHub — reference schema dump
--
-- Source      : pg_dump --schema-only, PostgreSQL 16.15 (postgres:16-alpine) on the
--               staging VPS, taken 2026-08-26.
-- Reflects    : forward-only migrations 001_core .. 005_change_retention_floor, i.e.
--               every row present in schema_migrations at the time of the dump.
-- Purpose     : a reviewable picture of what the migrations actually produce, so a
--               reader can diff intent against reality without a database.
--
-- NOT A MIGRATION. Do not apply this file. backend/datahub/migrations/*.sql is the
-- only thing that may touch a database; this file is regenerated from them, never
-- the other way round. It is also schema-only, so it records no schema_migrations
-- rows — the "Reflects" line above is the only statement of which migrations built
-- it, and it must be updated whenever a new migration lands.
--
-- The pg_dump restrict/unrestrict guards were dropped: they carry a per-dump random
-- token, so keeping them would make every regeneration a spurious diff, and they are
-- psql meta-commands that no other client can parse.
--

--
-- PostgreSQL database dump
--


-- Dumped from database version 16.15
-- Dumped by pg_dump version 16.15

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: create_datahub_site(uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.create_datahub_site(p_site_id uuid, p_site_code text) RETURNS void
    LANGUAGE plpgsql
    AS $$
BEGIN
    p_site_code := upper(btrim(p_site_code));
    IF p_site_code = '' THEN
        RAISE EXCEPTION 'site code cannot be blank';
    END IF;
    INSERT INTO sites (id, site_code)
    VALUES (p_site_id, p_site_code);

    INSERT INTO site_fetch_leases (site_id)
    VALUES (p_site_id);

    INSERT INTO site_change_counters (site_id, change_seq)
    VALUES (p_site_id, 0);
END;
$$;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: audit_logs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.audit_logs (
    id bigint NOT NULL,
    site_id uuid,
    actor text NOT NULL,
    action text NOT NULL,
    at timestamp with time zone DEFAULT now() NOT NULL,
    payload jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_audit_logs_action_not_blank CHECK ((length(btrim(action)) > 0)),
    CONSTRAINT ck_audit_logs_actor_not_blank CHECK ((length(btrim(actor)) > 0))
);


--
-- Name: audit_logs_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.audit_logs ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.audit_logs_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: dashboard_changes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.dashboard_changes (
    site_id uuid NOT NULL,
    change_seq bigint NOT NULL,
    entity_type text NOT NULL,
    entity_key text NOT NULL,
    operation text NOT NULL,
    change_at timestamp with time zone DEFAULT now() NOT NULL,
    body jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_dashboard_changes_operation CHECK ((operation = ANY (ARRAY['upsert'::text, 'delete'::text, 'resync'::text]))),
    CONSTRAINT ck_dashboard_changes_seq_positive CHECK ((change_seq > 0))
);


--
-- Name: devices; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.devices (
    id uuid NOT NULL,
    site_id uuid NOT NULL,
    name text NOT NULL,
    credential_hash text NOT NULL,
    token_version integer DEFAULT 1 NOT NULL,
    status text DEFAULT 'active'::text NOT NULL,
    last_seen_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_devices_credential_hash_not_blank CHECK ((length(btrim(credential_hash)) > 0)),
    CONSTRAINT ck_devices_name_not_blank CHECK ((length(btrim(name)) > 0)),
    CONSTRAINT ck_devices_status CHECK ((status = ANY (ARRAY['active'::text, 'revoked'::text, 'disabled'::text]))),
    CONSTRAINT ck_devices_token_version_positive CHECK ((token_version > 0))
);


--
-- Name: idempotency_records; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.idempotency_records (
    site_id uuid NOT NULL,
    key text NOT NULL,
    body_sha256 text NOT NULL,
    response jsonb NOT NULL,
    status_code integer NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    CONSTRAINT ck_idempotency_body_hash_not_blank CHECK ((length(btrim(body_sha256)) > 0)),
    CONSTRAINT ck_idempotency_key_not_blank CHECK ((length(btrim(key)) > 0))
);


--
-- Name: jms_event_policies; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.jms_event_policies (
    reducer_version integer NOT NULL,
    scan_type_code integer NOT NULL,
    event_kind text NOT NULL,
    CONSTRAINT ck_jms_event_policies_kind CHECK ((event_kind = ANY (ARRAY['state_transition'::text, 'activity'::text, 'inventory'::text, 'communication'::text])))
);


--
-- Name: retention_policies; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.retention_policies (
    id bigint NOT NULL,
    site_id uuid,
    table_name text NOT NULL,
    clock_column text NOT NULL,
    hot_after interval,
    archive_after interval,
    delete_after interval,
    CONSTRAINT ck_retention_clock_column_not_blank CHECK ((length(btrim(clock_column)) > 0)),
    CONSTRAINT ck_retention_intervals_nonnegative CHECK ((((hot_after IS NULL) OR (hot_after >= '00:00:00'::interval)) AND ((archive_after IS NULL) OR (archive_after >= '00:00:00'::interval)) AND ((delete_after IS NULL) OR (delete_after >= '00:00:00'::interval)))),
    CONSTRAINT ck_retention_table_name_not_blank CHECK ((length(btrim(table_name)) > 0))
);


--
-- Name: retention_policies_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.retention_policies ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.retention_policies_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: schema_migrations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.schema_migrations (
    version text NOT NULL,
    applied_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: site_change_counters; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.site_change_counters (
    site_id uuid NOT NULL,
    change_seq bigint DEFAULT 0 NOT NULL,
    pruned_through_seq bigint DEFAULT 0 NOT NULL,
    CONSTRAINT ck_site_change_counters_pruned_range CHECK (((pruned_through_seq >= 0) AND (pruned_through_seq <= change_seq))),
    CONSTRAINT ck_site_change_counters_seq_nonnegative CHECK ((change_seq >= 0))
);


--
-- Name: site_fetch_leases; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.site_fetch_leases (
    site_id uuid NOT NULL,
    leader_device_id uuid,
    leader_term bigint DEFAULT 0 NOT NULL,
    lease_expires_at timestamp with time zone DEFAULT '-infinity'::timestamp with time zone NOT NULL,
    last_seen_at timestamp with time zone,
    CONSTRAINT ck_site_fetch_leases_term_nonnegative CHECK ((leader_term >= 0))
);


--
-- Name: sites; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sites (
    id uuid NOT NULL,
    site_code text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_sites_site_code_not_blank CHECK ((length(btrim(site_code)) > 0))
);


--
-- Name: waybill_projections; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.waybill_projections (
    site_id uuid NOT NULL,
    waybill_no text NOT NULL,
    state_code integer,
    state_name text,
    state_event_at timestamp with time zone,
    state_fingerprint text,
    state_event_id bigint,
    state_kind text,
    state_status text,
    state_payload jsonb DEFAULT '{}'::jsonb NOT NULL,
    last_activity_code integer,
    last_activity_name text,
    last_activity_kind text,
    last_activity_at timestamp with time zone,
    last_activity_fingerprint text,
    last_activity_event_id bigint,
    last_activity_status text,
    last_activity_payload jsonb DEFAULT '{}'::jsonb NOT NULL,
    inventory_code integer,
    inventory_name text,
    inventory_event_at timestamp with time zone,
    inventory_fingerprint text,
    inventory_event_id bigint,
    inventory_status text,
    inventory_payload jsonb DEFAULT '{}'::jsonb NOT NULL,
    payload jsonb DEFAULT '{}'::jsonb NOT NULL,
    reducer_version integer DEFAULT 1 NOT NULL,
    version bigint DEFAULT 1 NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_waybill_projections_reducer_version_positive CHECK ((reducer_version > 0)),
    CONSTRAINT ck_waybill_projections_version_positive CHECK ((version > 0)),
    CONSTRAINT ck_waybill_projections_waybill_not_blank CHECK ((length(btrim(waybill_no)) > 0))
);


--
-- Name: waybill_scan_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.waybill_scan_events (
    id bigint NOT NULL,
    site_id uuid NOT NULL,
    waybill_no text NOT NULL,
    event_fingerprint text NOT NULL,
    fingerprint_version smallint DEFAULT 1 NOT NULL,
    event_occurred_at timestamp with time zone NOT NULL,
    ingested_at timestamp with time zone DEFAULT now() NOT NULL,
    scan_type_code integer,
    scan_type_name text,
    status text,
    network_code text,
    operator_code text,
    package_number text,
    task_code text,
    payload jsonb NOT NULL,
    CONSTRAINT ck_waybill_events_fingerprint_not_blank CHECK ((length(btrim(event_fingerprint)) > 0)),
    CONSTRAINT ck_waybill_events_fingerprint_version_positive CHECK ((fingerprint_version > 0)),
    CONSTRAINT ck_waybill_events_waybill_not_blank CHECK ((length(btrim(waybill_no)) > 0))
);


--
-- Name: waybill_scan_events_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.waybill_scan_events ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.waybill_scan_events_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: audit_logs audit_logs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.audit_logs
    ADD CONSTRAINT audit_logs_pkey PRIMARY KEY (id);


--
-- Name: dashboard_changes dashboard_changes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.dashboard_changes
    ADD CONSTRAINT dashboard_changes_pkey PRIMARY KEY (site_id, change_seq);


--
-- Name: devices devices_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.devices
    ADD CONSTRAINT devices_pkey PRIMARY KEY (id);


--
-- Name: idempotency_records idempotency_records_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.idempotency_records
    ADD CONSTRAINT idempotency_records_pkey PRIMARY KEY (site_id, key);


--
-- Name: jms_event_policies jms_event_policies_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.jms_event_policies
    ADD CONSTRAINT jms_event_policies_pkey PRIMARY KEY (reducer_version, scan_type_code);


--
-- Name: retention_policies retention_policies_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.retention_policies
    ADD CONSTRAINT retention_policies_pkey PRIMARY KEY (id);


--
-- Name: schema_migrations schema_migrations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.schema_migrations
    ADD CONSTRAINT schema_migrations_pkey PRIMARY KEY (version);


--
-- Name: site_change_counters site_change_counters_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.site_change_counters
    ADD CONSTRAINT site_change_counters_pkey PRIMARY KEY (site_id);


--
-- Name: site_fetch_leases site_fetch_leases_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.site_fetch_leases
    ADD CONSTRAINT site_fetch_leases_pkey PRIMARY KEY (site_id);


--
-- Name: sites sites_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sites
    ADD CONSTRAINT sites_pkey PRIMARY KEY (id);


--
-- Name: sites sites_site_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sites
    ADD CONSTRAINT sites_site_code_key UNIQUE (site_code);


--
-- Name: devices uq_devices_site_name; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.devices
    ADD CONSTRAINT uq_devices_site_name UNIQUE (site_id, name);


--
-- Name: waybill_scan_events uq_waybill_events_site_fingerprint; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.waybill_scan_events
    ADD CONSTRAINT uq_waybill_events_site_fingerprint UNIQUE (site_id, event_fingerprint);


--
-- Name: waybill_projections waybill_projections_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.waybill_projections
    ADD CONSTRAINT waybill_projections_pkey PRIMARY KEY (site_id, waybill_no);


--
-- Name: waybill_scan_events waybill_scan_events_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.waybill_scan_events
    ADD CONSTRAINT waybill_scan_events_pkey PRIMARY KEY (id);


--
-- Name: ix_audit_logs_site_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_audit_logs_site_at ON public.audit_logs USING btree (site_id, at);


--
-- Name: ix_devices_site_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_devices_site_status ON public.devices USING btree (site_id, status);


--
-- Name: ix_idempotency_records_expiry; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_idempotency_records_expiry ON public.idempotency_records USING btree (expires_at);


--
-- Name: ix_waybill_scan_events_site_waybill_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_waybill_scan_events_site_waybill_time ON public.waybill_scan_events USING btree (site_id, waybill_no, event_occurred_at);


--
-- Name: ux_retention_policies_global_table; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_retention_policies_global_table ON public.retention_policies USING btree (table_name) WHERE (site_id IS NULL);


--
-- Name: ux_retention_policies_site_table; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_retention_policies_site_table ON public.retention_policies USING btree (site_id, table_name) WHERE (site_id IS NOT NULL);


--
-- Name: audit_logs audit_logs_site_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.audit_logs
    ADD CONSTRAINT audit_logs_site_id_fkey FOREIGN KEY (site_id) REFERENCES public.sites(id);


--
-- Name: dashboard_changes dashboard_changes_site_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.dashboard_changes
    ADD CONSTRAINT dashboard_changes_site_id_fkey FOREIGN KEY (site_id) REFERENCES public.sites(id);


--
-- Name: devices devices_site_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.devices
    ADD CONSTRAINT devices_site_id_fkey FOREIGN KEY (site_id) REFERENCES public.sites(id);


--
-- Name: idempotency_records idempotency_records_site_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.idempotency_records
    ADD CONSTRAINT idempotency_records_site_id_fkey FOREIGN KEY (site_id) REFERENCES public.sites(id);


--
-- Name: retention_policies retention_policies_site_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.retention_policies
    ADD CONSTRAINT retention_policies_site_id_fkey FOREIGN KEY (site_id) REFERENCES public.sites(id);


--
-- Name: site_change_counters site_change_counters_site_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.site_change_counters
    ADD CONSTRAINT site_change_counters_site_id_fkey FOREIGN KEY (site_id) REFERENCES public.sites(id);


--
-- Name: site_fetch_leases site_fetch_leases_leader_device_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.site_fetch_leases
    ADD CONSTRAINT site_fetch_leases_leader_device_id_fkey FOREIGN KEY (leader_device_id) REFERENCES public.devices(id) ON DELETE RESTRICT;


--
-- Name: site_fetch_leases site_fetch_leases_site_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.site_fetch_leases
    ADD CONSTRAINT site_fetch_leases_site_id_fkey FOREIGN KEY (site_id) REFERENCES public.sites(id);


--
-- Name: waybill_projections waybill_projections_site_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.waybill_projections
    ADD CONSTRAINT waybill_projections_site_id_fkey FOREIGN KEY (site_id) REFERENCES public.sites(id);


--
-- Name: waybill_scan_events waybill_scan_events_site_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.waybill_scan_events
    ADD CONSTRAINT waybill_scan_events_site_id_fkey FOREIGN KEY (site_id) REFERENCES public.sites(id);


--
-- PostgreSQL database dump complete
--



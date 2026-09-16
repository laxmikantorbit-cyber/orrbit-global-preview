BEGIN;

CREATE SCHEMA IF NOT EXISTS businessos_crm;

CREATE TABLE IF NOT EXISTS businessos_crm.leads (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    organisation_id uuid NOT NULL,
    title text NOT NULL,
    attribution jsonb NOT NULL,
    contact_name text NULL,
    mobile_number text NULL,
    email text NULL,
    product_interest text NULL,
    notes text NULL,
    status integer NOT NULL,
    priority integer NOT NULL,
    unqualified_reason text NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    last_contact_at_utc timestamptz NULL,
    next_follow_up_at_utc timestamptz NULL,
    tags jsonb NOT NULL DEFAULT '[]'::jsonb
);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_leads_tenant
    ON businessos_crm.leads(tenant_id, created_at_utc DESC);

CREATE TABLE IF NOT EXISTS businessos_crm.activities (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    lead_id uuid NOT NULL,
    type integer NOT NULL,
    summary text NOT NULL,
    details text NULL,
    actor_user_id uuid NULL,
    occurred_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_activities_lead
    ON businessos_crm.activities(tenant_id, lead_id, occurred_at_utc DESC);

CREATE TABLE IF NOT EXISTS businessos_crm.follow_ups (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    lead_id uuid NOT NULL,
    due_at_utc timestamptz NOT NULL,
    channel integer NOT NULL,
    purpose text NOT NULL,
    owner_user_id uuid NULL,
    status integer NOT NULL,
    outcome text NULL,
    created_at_utc timestamptz NOT NULL,
    completed_at_utc timestamptz NULL
);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_followups_tenant
    ON businessos_crm.follow_ups(tenant_id, status, due_at_utc);

CREATE TABLE IF NOT EXISTS businessos_crm.tasks (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    lead_id uuid NULL,
    title text NOT NULL,
    details text NULL,
    due_at_utc timestamptz NULL,
    priority integer NOT NULL,
    assignee_user_id uuid NULL,
    status integer NOT NULL,
    created_at_utc timestamptz NOT NULL,
    completed_at_utc timestamptz NULL
);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_tasks_tenant
    ON businessos_crm.tasks(tenant_id, status, due_at_utc);

CREATE TABLE IF NOT EXISTS businessos_crm.accounts (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    name text NOT NULL,
    legal_name text NULL,
    gstin text NULL,
    display_code text NULL,
    status integer NOT NULL,
    roles jsonb NOT NULL DEFAULT '[]'::jsonb,
    contacts jsonb NOT NULL DEFAULT '[]'::jsonb,
    addresses jsonb NOT NULL DEFAULT '[]'::jsonb
);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_accounts_tenant
    ON businessos_crm.accounts(tenant_id, name);

CREATE TABLE IF NOT EXISTS businessos_crm.opportunities (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    organisation_id uuid NOT NULL,
    originating_lead_id uuid NULL,
    owner_user_id uuid NULL,
    title text NOT NULL,
    stage integer NOT NULL,
    estimated_value numeric(18,2) NOT NULL,
    currency_code varchar(3) NOT NULL,
    probability_percent integer NOT NULL,
    expected_close_date date NULL,
    loss_reason text NULL
);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_opportunities_tenant
    ON businessos_crm.opportunities(tenant_id, stage);

CREATE TABLE IF NOT EXISTS businessos_crm.team_members (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    display_name text NOT NULL,
    email text NOT NULL,
    mobile_number text NULL,
    role integer NOT NULL,
    active boolean NOT NULL,
    created_at_utc timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_businessos_crm_team_email
    ON businessos_crm.team_members(tenant_id, lower(email));
CREATE INDEX IF NOT EXISTS ix_businessos_crm_team_tenant
    ON businessos_crm.team_members(tenant_id, active, display_name);

-- Runtime role receives data access only; schema ownership/CREATE stays with the owner.
REVOKE ALL ON SCHEMA businessos_crm FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA businessos_crm FROM PUBLIC;

GRANT USAGE ON SCHEMA businessos_crm TO businessos_rls;
GRANT SELECT, INSERT, UPDATE, DELETE
    ON ALL TABLES IN SCHEMA businessos_crm
    TO businessos_rls;

ALTER DEFAULT PRIVILEGES FOR ROLE businessos_owner IN SCHEMA businessos_crm
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO businessos_rls;

COMMIT;

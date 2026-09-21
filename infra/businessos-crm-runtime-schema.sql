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
    tags jsonb NOT NULL DEFAULT '[]'::jsonb,
    estimated_value numeric(18,2) NULL
);
ALTER TABLE businessos_crm.leads
    ADD COLUMN IF NOT EXISTS estimated_value numeric(18,2) NULL;
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
    addresses jsonb NOT NULL DEFAULT '[]'::jsonb,
    groups jsonb NOT NULL DEFAULT '[]'::jsonb
);
ALTER TABLE businessos_crm.accounts
    ADD COLUMN IF NOT EXISTS groups jsonb NOT NULL DEFAULT '[]'::jsonb;
CREATE INDEX IF NOT EXISTS ix_businessos_crm_accounts_tenant
    ON businessos_crm.accounts(tenant_id, name);

CREATE TABLE IF NOT EXISTS businessos_crm.opportunities (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    organisation_id uuid NOT NULL,
    originating_lead_id uuid NULL,
    owner_user_id uuid NULL,
    title text NOT NULL,
    product_service text NULL,
    stage integer NOT NULL,
    estimated_value numeric(18,2) NOT NULL,
    currency_code varchar(3) NOT NULL,
    probability_percent integer NOT NULL,
    expected_close_date date NULL,
    loss_reason text NULL
);
ALTER TABLE businessos_crm.opportunities
    ADD COLUMN IF NOT EXISTS product_service text NULL;
CREATE INDEX IF NOT EXISTS ix_businessos_crm_opportunities_tenant
    ON businessos_crm.opportunities(tenant_id, stage);

CREATE TABLE IF NOT EXISTS businessos_crm.sales_documents (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    account_id uuid NOT NULL,
    opportunity_id uuid NULL,
    kind integer NOT NULL,
    document_number text NOT NULL,
    subject text NOT NULL,
    status integer NOT NULL,
    currency_code varchar(3) NOT NULL,
    issue_date date NOT NULL,
    expiry_date date NULL,
    discount_percent numeric(5,2) NOT NULL DEFAULT 0,
    notes text NULL,
    terms text NULL,
    lines jsonb NOT NULL DEFAULT '[]'::jsonb,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_businessos_crm_sales_document_number
    ON businessos_crm.sales_documents(tenant_id, kind, document_number);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_sales_documents_tenant
    ON businessos_crm.sales_documents(tenant_id, kind, status, issue_date DESC);

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

CREATE TABLE IF NOT EXISTS businessos_crm.saved_views (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL,
    module text NOT NULL,
    name text NOT NULL,
    filters_json text NOT NULL,
    is_default boolean NOT NULL DEFAULT false,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_businessos_crm_saved_views_name
    ON businessos_crm.saved_views(tenant_id, user_id, module, lower(name));
CREATE INDEX IF NOT EXISTS ix_businessos_crm_saved_views_user
    ON businessos_crm.saved_views(tenant_id, user_id, module);

CREATE TABLE IF NOT EXISTS businessos_crm.master_items (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    category text NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    active boolean NOT NULL DEFAULT true,
    sort_order integer NOT NULL DEFAULT 0,
    updated_at_utc timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_businessos_crm_master_code
    ON businessos_crm.master_items(tenant_id, category, code);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_master_category
    ON businessos_crm.master_items(tenant_id, category, active, sort_order);

CREATE TABLE IF NOT EXISTS businessos_crm.audit_events (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    actor_user_id uuid NULL,
    action text NOT NULL,
    entity_type text NOT NULL,
    entity_id text NULL,
    detail text NULL,
    created_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_audit_tenant
    ON businessos_crm.audit_events(tenant_id, created_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_audit_entity
    ON businessos_crm.audit_events(tenant_id, entity_type, entity_id, created_at_utc DESC);

CREATE TABLE IF NOT EXISTS businessos_crm.notifications (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL,
    source_key text NOT NULL,
    type text NOT NULL,
    title text NOT NULL,
    detail text NOT NULL,
    lead_id uuid NULL,
    record_id uuid NULL,
    due_at_utc timestamptz NULL,
    severity text NOT NULL,
    is_read boolean NOT NULL DEFAULT false,
    active boolean NOT NULL DEFAULT true,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_businessos_crm_notifications_source
    ON businessos_crm.notifications(tenant_id, user_id, source_key);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_notifications_inbox
    ON businessos_crm.notifications(tenant_id, user_id, active, is_read, due_at_utc);

CREATE TABLE IF NOT EXISTS businessos_crm.entity_activities (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    entity_type text NOT NULL,
    entity_id uuid NOT NULL,
    contact_id uuid NULL,
    channel text NOT NULL,
    summary text NOT NULL,
    details text NULL,
    actor_user_id uuid NULL,
    occurred_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_entity_activities_entity
    ON businessos_crm.entity_activities(tenant_id, entity_type, entity_id, occurred_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_entity_activities_contact
    ON businessos_crm.entity_activities(tenant_id, contact_id, occurred_at_utc DESC)
    WHERE contact_id IS NOT NULL;

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

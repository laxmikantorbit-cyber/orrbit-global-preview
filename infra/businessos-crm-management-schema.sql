BEGIN;

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

REVOKE ALL ON businessos_crm.saved_views, businessos_crm.master_items, businessos_crm.audit_events FROM PUBLIC;
GRANT SELECT, INSERT, UPDATE, DELETE
    ON businessos_crm.saved_views, businessos_crm.master_items, businessos_crm.audit_events
    TO businessos_rls;

ALTER DEFAULT PRIVILEGES FOR ROLE businessos_owner IN SCHEMA businessos_crm
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO businessos_rls;

COMMIT;

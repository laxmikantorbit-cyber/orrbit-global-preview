BEGIN;

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

REVOKE ALL ON businessos_crm.notifications FROM PUBLIC;
GRANT SELECT, INSERT, UPDATE, DELETE ON businessos_crm.notifications TO businessos_rls;

ALTER DEFAULT PRIVILEGES FOR ROLE businessos_owner IN SCHEMA businessos_crm
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO businessos_rls;

COMMIT;

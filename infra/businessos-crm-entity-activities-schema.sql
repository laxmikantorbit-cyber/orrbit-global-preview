BEGIN;

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

REVOKE ALL ON businessos_crm.entity_activities FROM PUBLIC;
GRANT SELECT, INSERT, UPDATE, DELETE ON businessos_crm.entity_activities TO businessos_rls;

ALTER DEFAULT PRIVILEGES FOR ROLE businessos_owner IN SCHEMA businessos_crm
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO businessos_rls;

COMMIT;

CREATE TABLE IF NOT EXISTS organisations (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    name text NOT NULL CHECK (length(btrim(name)) > 0),
    legal_name text NULL,
    gstin text NULL,
    display_code text NULL,
    status smallint NOT NULL CHECK (status IN (1,2,3)),
    UNIQUE (tenant_id, id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_organisations_tenant_display_code
    ON organisations (tenant_id, upper(display_code))
    WHERE display_code IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_organisations_tenant_name
    ON organisations (tenant_id, name);

CREATE TABLE IF NOT EXISTS organisation_roles (
    tenant_id uuid NOT NULL,
    organisation_id uuid NOT NULL,
    role_code smallint NOT NULL CHECK (role_code IN (1,2,3)),
    PRIMARY KEY (tenant_id, organisation_id, role_code),
    FOREIGN KEY (tenant_id, organisation_id)
        REFERENCES organisations(tenant_id, id) ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS organisation_contacts (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    organisation_id uuid NOT NULL,
    name text NOT NULL CHECK (length(btrim(name)) > 0),
    email text NULL,
    phone text NULL,
    designation text NULL,
    is_primary boolean NOT NULL DEFAULT false,
    FOREIGN KEY (tenant_id, organisation_id)
        REFERENCES organisations(tenant_id, id) ON DELETE CASCADE
);

ALTER TABLE organisation_contacts
    ADD COLUMN IF NOT EXISTS designation text NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_org_contacts_one_primary
    ON organisation_contacts (tenant_id, organisation_id)
    WHERE is_primary;

CREATE INDEX IF NOT EXISTS ix_org_contacts_tenant_org
    ON organisation_contacts (tenant_id, organisation_id);

CREATE TABLE IF NOT EXISTS organisation_addresses (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    organisation_id uuid NOT NULL,
    line1 text NOT NULL CHECK (length(btrim(line1)) > 0),
    line2 text NULL,
    city text NOT NULL,
    state text NOT NULL,
    postal_code text NOT NULL,
    country_code text NOT NULL CHECK (length(btrim(country_code)) = 2),
    state_code text NULL CHECK (state_code IS NULL OR state_code ~ '^[0-9]{2}$'),
    is_primary boolean NOT NULL DEFAULT false,
    FOREIGN KEY (tenant_id, organisation_id)
        REFERENCES organisations(tenant_id, id) ON DELETE CASCADE
);

ALTER TABLE organisation_addresses
    ADD COLUMN IF NOT EXISTS state_code text NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_organisation_addresses_state_code'
          AND conrelid = 'organisation_addresses'::regclass
    ) THEN
        ALTER TABLE organisation_addresses
            ADD CONSTRAINT ck_organisation_addresses_state_code
            CHECK (state_code IS NULL OR state_code ~ '^[0-9]{2}$');
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_org_addresses_one_primary
    ON organisation_addresses (tenant_id, organisation_id)
    WHERE is_primary;

CREATE INDEX IF NOT EXISTS ix_org_addresses_tenant_org
    ON organisation_addresses (tenant_id, organisation_id);

ALTER TABLE organisations ENABLE ROW LEVEL SECURITY;
ALTER TABLE organisations FORCE ROW LEVEL SECURITY;
ALTER TABLE organisation_roles ENABLE ROW LEVEL SECURITY;
ALTER TABLE organisation_roles FORCE ROW LEVEL SECURITY;
ALTER TABLE organisation_contacts ENABLE ROW LEVEL SECURITY;
ALTER TABLE organisation_contacts FORCE ROW LEVEL SECURITY;
ALTER TABLE organisation_addresses ENABLE ROW LEVEL SECURITY;
ALTER TABLE organisation_addresses FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS organisations_tenant_policy ON organisations;
CREATE POLICY organisations_tenant_policy ON organisations
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid)
    WITH CHECK (tenant_id = current_setting('app.tenant_id', true)::uuid);
DROP POLICY IF EXISTS organisation_roles_tenant_policy ON organisation_roles;
CREATE POLICY organisation_roles_tenant_policy ON organisation_roles
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid)
    WITH CHECK (tenant_id = current_setting('app.tenant_id', true)::uuid);

DROP POLICY IF EXISTS organisation_contacts_tenant_policy ON organisation_contacts;
CREATE POLICY organisation_contacts_tenant_policy ON organisation_contacts
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid)
    WITH CHECK (tenant_id = current_setting('app.tenant_id', true)::uuid);

DROP POLICY IF EXISTS organisation_addresses_tenant_policy ON organisation_addresses;
CREATE POLICY organisation_addresses_tenant_policy ON organisation_addresses
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid)
    WITH CHECK (tenant_id = current_setting('app.tenant_id', true)::uuid);

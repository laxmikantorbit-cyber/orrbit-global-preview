CREATE TABLE IF NOT EXISTS tenants (
    id uuid PRIMARY KEY,
    code text NOT NULL UNIQUE,
    name text NOT NULL,
    status integer NOT NULL CHECK (status IN (1,2,3))
);

CREATE TABLE IF NOT EXISTS user_identities (
    id uuid PRIMARY KEY,
    subject text NOT NULL UNIQUE,
    email text NOT NULL,
    display_name text NOT NULL,
    active boolean NOT NULL DEFAULT true
);

CREATE TABLE IF NOT EXISTS tenant_memberships (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES user_identities(id),
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    role_code text NOT NULL,
    status integer NOT NULL CHECK (status IN (1,2,3,4)),
    CONSTRAINT uq_membership_user_tenant UNIQUE (user_id, tenant_id)
);

CREATE INDEX IF NOT EXISTS ix_memberships_tenant_status
    ON tenant_memberships(tenant_id, status);

CREATE INDEX IF NOT EXISTS ix_memberships_user_status
    ON tenant_memberships(user_id, status);

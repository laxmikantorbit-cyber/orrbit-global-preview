DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'bos_owner') THEN
        CREATE ROLE bos_owner NOLOGIN;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'bos_app') THEN
        CREATE ROLE bos_app LOGIN;
    END IF;
END $$;

INSERT INTO tenants(id, code, name, status) VALUES
('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'TENANT-A', 'Tenant A', 1),
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'TENANT-B', 'Tenant B', 1)
ON CONFLICT (id) DO NOTHING;

INSERT INTO organisations(id, tenant_id, name, status) VALUES
('11111111-1111-1111-1111-111111111111', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Alpha Customer', 1),
('22222222-2222-2222-2222-222222222222', 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'Beta Customer', 1)
ON CONFLICT (id) DO NOTHING;
INSERT INTO organisation_roles(tenant_id, organisation_id, role_code) VALUES
('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', '11111111-1111-1111-1111-111111111111', 1),
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', '22222222-2222-2222-2222-222222222222', 1)
ON CONFLICT DO NOTHING;

ALTER TABLE organisations OWNER TO bos_owner;
ALTER TABLE organisation_roles OWNER TO bos_owner;
ALTER TABLE organisation_contacts OWNER TO bos_owner;
ALTER TABLE organisation_addresses OWNER TO bos_owner;

GRANT SELECT, INSERT, UPDATE, DELETE ON organisations TO bos_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON organisation_roles TO bos_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON organisation_contacts TO bos_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON organisation_addresses TO bos_app;

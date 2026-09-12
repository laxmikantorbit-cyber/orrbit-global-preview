CREATE ROLE businessos_app LOGIN
  NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;

CREATE TABLE poc_customers (
    id uuid PRIMARY KEY,
    tenant_id text NOT NULL,
    name text NOT NULL,
    email text NOT NULL
);

ALTER TABLE poc_customers ENABLE ROW LEVEL SECURITY;
ALTER TABLE poc_customers FORCE ROW LEVEL SECURITY;

CREATE POLICY poc_customers_tenant_isolation
ON poc_customers
USING (tenant_id = current_setting('app.tenant_id', true))
WITH CHECK (tenant_id = current_setting('app.tenant_id', true));

REVOKE ALL ON TABLE poc_customers FROM PUBLIC;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE poc_customers TO businessos_app;

INSERT INTO poc_customers (id, tenant_id, name, email) VALUES
('11111111-1111-1111-1111-111111111111', 'TENANT-A', 'Alpha Repair', 'alpha@example.test'),
('22222222-2222-2222-2222-222222222222', 'TENANT-B', 'Beta Repair', 'beta@example.test');
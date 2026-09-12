CREATE TABLE IF NOT EXISTS poc_customers (
    id uuid PRIMARY KEY,
    tenant_id text NOT NULL,
    name text NOT NULL,
    email text NOT NULL
);

ALTER TABLE poc_customers ENABLE ROW LEVEL SECURITY;
ALTER TABLE poc_customers FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS poc_customers_tenant_isolation ON poc_customers;
CREATE POLICY poc_customers_tenant_isolation
ON poc_customers
USING (tenant_id = current_setting('app.tenant_id', true))
WITH CHECK (tenant_id = current_setting('app.tenant_id', true));

CREATE INDEX IF NOT EXISTS ix_poc_customers_tenant_id
ON poc_customers (tenant_id);

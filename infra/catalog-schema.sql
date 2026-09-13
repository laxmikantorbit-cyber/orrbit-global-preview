CREATE TABLE IF NOT EXISTS catalog_products (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    code text NOT NULL,
    name text NOT NULL,
    status integer NOT NULL CHECK (status IN (1,2,3)),
    CONSTRAINT uq_catalog_product_tenant_id UNIQUE (tenant_id, id),
    CONSTRAINT uq_catalog_product_code UNIQUE (tenant_id, code)
);

CREATE TABLE IF NOT EXISTS catalog_plans (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    product_id uuid NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    status integer NOT NULL CHECK (status IN (1,2,3)),
    CONSTRAINT fk_catalog_plan_product_tenant
      FOREIGN KEY (tenant_id, product_id)
      REFERENCES catalog_products(tenant_id, id),
    CONSTRAINT uq_catalog_plan_tenant_id UNIQUE (tenant_id, id),
    CONSTRAINT uq_catalog_plan_code UNIQUE (tenant_id, product_id, code)
);

CREATE TABLE IF NOT EXISTS catalog_plan_versions (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    plan_id uuid NOT NULL,
    version_number integer NOT NULL CHECK (version_number > 0),
    amount numeric(18,2) NOT NULL CHECK (amount >= 0),
    currency_code text NOT NULL CHECK (char_length(currency_code) = 3),
    billing_cycle integer NOT NULL CHECK (billing_cycle IN (1,2,3)),
    term_months integer NULL CHECK (term_months IS NULL OR term_months > 0),
    desktop_device_limit integer NOT NULL CHECK (desktop_device_limit >= 0),
    location_limit integer NOT NULL CHECK (location_limit >= 0),
    web_admin_seats integer NOT NULL CHECK (web_admin_seats >= 0),
    field_staff_seats integer NOT NULL CHECK (field_staff_seats >= 0),
    multi_location_cloud boolean NOT NULL,
    features jsonb NOT NULL DEFAULT '[]'::jsonb CHECK (jsonb_typeof(features) = 'array'),
    effective_from_utc timestamptz NOT NULL,
    CONSTRAINT fk_catalog_version_plan_tenant
      FOREIGN KEY (tenant_id, plan_id)
      REFERENCES catalog_plans(tenant_id, id),
    CONSTRAINT uq_catalog_plan_version UNIQUE (tenant_id, plan_id, version_number)
);

CREATE INDEX IF NOT EXISTS ix_catalog_products_tenant_status
    ON catalog_products(tenant_id, status);
CREATE INDEX IF NOT EXISTS ix_catalog_plans_tenant_product
    ON catalog_plans(tenant_id, product_id, status);
CREATE INDEX IF NOT EXISTS ix_catalog_versions_tenant_plan
    ON catalog_plan_versions(tenant_id, plan_id, version_number DESC);

ALTER TABLE catalog_products ENABLE ROW LEVEL SECURITY;
ALTER TABLE catalog_products FORCE ROW LEVEL SECURITY;
ALTER TABLE catalog_plans ENABLE ROW LEVEL SECURITY;
ALTER TABLE catalog_plans FORCE ROW LEVEL SECURITY;
ALTER TABLE catalog_plan_versions ENABLE ROW LEVEL SECURITY;
ALTER TABLE catalog_plan_versions FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS catalog_products_tenant_policy ON catalog_products;
CREATE POLICY catalog_products_tenant_policy ON catalog_products
USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

DROP POLICY IF EXISTS catalog_plans_tenant_policy ON catalog_plans;
CREATE POLICY catalog_plans_tenant_policy ON catalog_plans
USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

DROP POLICY IF EXISTS catalog_versions_tenant_policy ON catalog_plan_versions;
CREATE POLICY catalog_versions_tenant_policy ON catalog_plan_versions
USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

CREATE OR REPLACE FUNCTION reject_catalog_plan_version_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'catalog plan versions are immutable';
END;
$$;

DROP TRIGGER IF EXISTS trg_catalog_plan_version_immutable ON catalog_plan_versions;
CREATE TRIGGER trg_catalog_plan_version_immutable
BEFORE UPDATE OR DELETE ON catalog_plan_versions
FOR EACH ROW EXECUTE FUNCTION reject_catalog_plan_version_mutation();

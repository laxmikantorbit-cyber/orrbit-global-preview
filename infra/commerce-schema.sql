CREATE UNIQUE INDEX IF NOT EXISTS uq_catalog_versions_tenant_id
    ON catalog_plan_versions(tenant_id, id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_sales_opportunities_tenant_id
    ON sales_opportunities(tenant_id, id);

CREATE TABLE IF NOT EXISTS commerce_quotes (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    organisation_id uuid NOT NULL,
    opportunity_id uuid NULL,
    plan_id uuid NOT NULL,
    plan_version_id uuid NOT NULL,
    plan_version_number integer NOT NULL CHECK (plan_version_number > 0),
    amount numeric(18,2) NOT NULL CHECK (amount >= 0),
    currency_code text NOT NULL CHECK (char_length(currency_code) = 3),
    billing_cycle integer NOT NULL CHECK (billing_cycle IN (1,2,3)),
    term_months integer NULL CHECK (term_months IS NULL OR term_months > 0),
    entitlement_snapshot jsonb NOT NULL CHECK (jsonb_typeof(entitlement_snapshot) = 'object'),
    created_at_utc timestamptz NOT NULL,
    valid_until_utc timestamptz NOT NULL,
    status integer NOT NULL CHECK (status IN (1,2,3)),
    CHECK (valid_until_utc > created_at_utc),
    UNIQUE (tenant_id, id),
    UNIQUE (tenant_id, id, organisation_id)
);
ALTER TABLE commerce_quotes ADD CONSTRAINT fk_commerce_quote_org_tenant
  FOREIGN KEY (tenant_id, organisation_id)
  REFERENCES organisations(tenant_id, id);
ALTER TABLE commerce_quotes ADD CONSTRAINT fk_commerce_quote_plan_version_tenant
  FOREIGN KEY (tenant_id, plan_version_id)
  REFERENCES catalog_plan_versions(tenant_id, id);
ALTER TABLE commerce_quotes ADD CONSTRAINT fk_commerce_quote_opportunity_tenant
  FOREIGN KEY (tenant_id, opportunity_id)
  REFERENCES sales_opportunities(tenant_id, id);

CREATE TABLE IF NOT EXISTS commerce_orders (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    organisation_id uuid NOT NULL,
    quote_id uuid NOT NULL,
    opportunity_id uuid NULL,
    plan_id uuid NOT NULL,
    plan_version_id uuid NOT NULL,
    amount numeric(18,2) NOT NULL CHECK (amount >= 0),
    currency_code text NOT NULL CHECK (char_length(currency_code) = 3),
    status integer NOT NULL CHECK (status IN (1,2,3,4)),
    payment_id text NULL,
    paid_at_utc timestamptz NULL,
    UNIQUE (tenant_id, id),
    UNIQUE (tenant_id, id, organisation_id)
);
ALTER TABLE commerce_orders ADD CONSTRAINT fk_commerce_order_quote_tenant
  FOREIGN KEY (tenant_id, quote_id, organisation_id)
  REFERENCES commerce_quotes(tenant_id, id, organisation_id);
ALTER TABLE commerce_orders ADD CONSTRAINT fk_commerce_order_opportunity_tenant
  FOREIGN KEY (tenant_id, opportunity_id)
  REFERENCES sales_opportunities(tenant_id, id);
ALTER TABLE commerce_orders ADD CONSTRAINT fk_commerce_order_plan_version_tenant
  FOREIGN KEY (tenant_id, plan_version_id)
  REFERENCES catalog_plan_versions(tenant_id, id);

CREATE UNIQUE INDEX IF NOT EXISTS uq_commerce_order_payment
  ON commerce_orders(tenant_id, payment_id) WHERE payment_id IS NOT NULL;

ALTER TABLE commerce_orders
  ADD COLUMN IF NOT EXISTS razorpay_order_id text NULL;
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conname = 'ck_commerce_order_razorpay_order_id') THEN
    ALTER TABLE commerce_orders
      ADD CONSTRAINT ck_commerce_order_razorpay_order_id
      CHECK (razorpay_order_id IS NULL OR length(btrim(razorpay_order_id)) > 0);
  END IF;
END $$;
CREATE UNIQUE INDEX IF NOT EXISTS ux_commerce_orders_tenant_razorpay_order
  ON commerce_orders(tenant_id, razorpay_order_id)
  WHERE razorpay_order_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS commerce_subscriptions (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    organisation_id uuid NOT NULL,
    order_id uuid NOT NULL,
    plan_id uuid NOT NULL,
    plan_version_id uuid NOT NULL,
    starts_on date NOT NULL,
    valid_until date NULL,
    entitlement_snapshot jsonb NOT NULL CHECK (jsonb_typeof(entitlement_snapshot) = 'object'),
    status integer NOT NULL CHECK (status IN (1,2)),
    CHECK (valid_until IS NULL OR valid_until >= starts_on),
    UNIQUE (tenant_id, id)
);
ALTER TABLE commerce_subscriptions ADD CONSTRAINT fk_commerce_subscription_order_tenant
  FOREIGN KEY (tenant_id, order_id, organisation_id)
  REFERENCES commerce_orders(tenant_id, id, organisation_id);
ALTER TABLE commerce_subscriptions ADD CONSTRAINT fk_commerce_subscription_plan_version_tenant
  FOREIGN KEY (tenant_id, plan_version_id)
  REFERENCES catalog_plan_versions(tenant_id, id);

CREATE INDEX IF NOT EXISTS ix_commerce_quotes_tenant_status
  ON commerce_quotes(tenant_id, status);
CREATE INDEX IF NOT EXISTS ix_commerce_orders_tenant_status
  ON commerce_orders(tenant_id, status);
CREATE INDEX IF NOT EXISTS ix_commerce_subscriptions_tenant_status
  ON commerce_subscriptions(tenant_id, status);

ALTER TABLE commerce_subscriptions
  ADD COLUMN IF NOT EXISTS license_id uuid NULL;
ALTER TABLE commerce_subscriptions
  ADD COLUMN IF NOT EXISTS product_code text NULL;
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conname = 'ck_commerce_subscription_product_code') THEN
    ALTER TABLE commerce_subscriptions
      ADD CONSTRAINT ck_commerce_subscription_product_code
      CHECK (product_code IS NULL OR length(btrim(product_code)) > 0);
  END IF;
END $$;
CREATE UNIQUE INDEX IF NOT EXISTS ux_commerce_subscriptions_tenant_license
  ON commerce_subscriptions(tenant_id, license_id)
  WHERE license_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_commerce_subscriptions_tenant_order
  ON commerce_subscriptions(tenant_id, order_id);

CREATE TABLE IF NOT EXISTS commerce_provider_order_routes (
    provider text NOT NULL CHECK (length(btrim(provider)) > 0),
    provider_order_id text NOT NULL CHECK (length(btrim(provider_order_id)) > 0),
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    commerce_order_id uuid NOT NULL,
    product_code text NOT NULL CHECK (length(btrim(product_code)) > 0),
    subscription_id uuid NULL,
    created_at_utc timestamptz NOT NULL,
    PRIMARY KEY (provider, provider_order_id),
    FOREIGN KEY (tenant_id, commerce_order_id)
      REFERENCES commerce_orders(tenant_id, id),
    FOREIGN KEY (tenant_id, subscription_id)
      REFERENCES commerce_subscriptions(tenant_id, id)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_commerce_provider_routes_tenant_provider_order
  ON commerce_provider_order_routes(tenant_id, provider, provider_order_id);

ALTER TABLE commerce_quotes ENABLE ROW LEVEL SECURITY;
ALTER TABLE commerce_quotes FORCE ROW LEVEL SECURITY;
ALTER TABLE commerce_orders ENABLE ROW LEVEL SECURITY;
ALTER TABLE commerce_orders FORCE ROW LEVEL SECURITY;
ALTER TABLE commerce_subscriptions ENABLE ROW LEVEL SECURITY;
ALTER TABLE commerce_subscriptions FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS commerce_quotes_tenant_policy ON commerce_quotes;
CREATE POLICY commerce_quotes_tenant_policy ON commerce_quotes
USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

DROP POLICY IF EXISTS commerce_orders_tenant_policy ON commerce_orders;
CREATE POLICY commerce_orders_tenant_policy ON commerce_orders
USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

DROP POLICY IF EXISTS commerce_subscriptions_tenant_policy ON commerce_subscriptions;
CREATE POLICY commerce_subscriptions_tenant_policy ON commerce_subscriptions
USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

CREATE TABLE IF NOT EXISTS commerce_subscription_renewals (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    subscription_id uuid NOT NULL,
    order_id uuid NOT NULL,
    paid_at_utc timestamptz NOT NULL,
    previous_valid_until date NULL,
    new_valid_until date NOT NULL,
    term_months integer NOT NULL CHECK (term_months > 0),
    plan_version_id uuid NOT NULL,
    CHECK (previous_valid_until IS NULL OR new_valid_until > previous_valid_until),
    UNIQUE (tenant_id, id),
    UNIQUE (tenant_id, order_id),
    FOREIGN KEY (tenant_id, subscription_id)
      REFERENCES commerce_subscriptions(tenant_id, id),
    FOREIGN KEY (tenant_id, order_id)
      REFERENCES commerce_orders(tenant_id, id),
    FOREIGN KEY (tenant_id, plan_version_id)
      REFERENCES catalog_plan_versions(tenant_id, id)
);

CREATE INDEX IF NOT EXISTS ix_commerce_renewals_tenant_subscription
  ON commerce_subscription_renewals(tenant_id, subscription_id);
ALTER TABLE commerce_subscription_renewals ENABLE ROW LEVEL SECURITY;
ALTER TABLE commerce_subscription_renewals FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS commerce_renewals_tenant_policy ON commerce_subscription_renewals;
CREATE POLICY commerce_renewals_tenant_policy ON commerce_subscription_renewals
USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

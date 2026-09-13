CREATE UNIQUE INDEX IF NOT EXISTS uq_crm_leads_tenant_id_id
    ON crm_leads(tenant_id, id);

CREATE TABLE IF NOT EXISTS sales_opportunities (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    organisation_id uuid NOT NULL,
    originating_lead_id uuid NULL,
    owner_user_id uuid NULL,
    title text NOT NULL,
    stage integer NOT NULL CHECK (stage IN (1,2,3,4,5,6)),
    estimated_value numeric(18,2) NOT NULL CHECK (estimated_value >= 0),
    currency_code varchar(3) NOT NULL CHECK (char_length(currency_code) = 3),
    probability_percent integer NOT NULL CHECK (probability_percent BETWEEN 0 AND 100),
    expected_close_date date NULL,
    loss_reason text NULL,
    CONSTRAINT ck_sales_lost_reason
      CHECK (stage <> 6 OR nullif(btrim(loss_reason), '') IS NOT NULL),
    CONSTRAINT ck_sales_won_positive
      CHECK (stage <> 5 OR estimated_value > 0)
);

ALTER TABLE sales_opportunities
    ADD CONSTRAINT fk_sales_opportunity_organisation_tenant
    FOREIGN KEY (tenant_id, organisation_id)
    REFERENCES organisations(tenant_id, id);

ALTER TABLE sales_opportunities
    ADD CONSTRAINT fk_sales_opportunity_lead_tenant
    FOREIGN KEY (tenant_id, originating_lead_id)
    REFERENCES crm_leads(tenant_id, id);

ALTER TABLE sales_opportunities
    ADD CONSTRAINT fk_sales_opportunity_owner_membership
    FOREIGN KEY (owner_user_id, tenant_id)
    REFERENCES tenant_memberships(user_id, tenant_id);

CREATE INDEX IF NOT EXISTS ix_sales_opportunities_tenant_stage
    ON sales_opportunities(tenant_id, stage);
CREATE INDEX IF NOT EXISTS ix_sales_opportunities_tenant_org
    ON sales_opportunities(tenant_id, organisation_id);

ALTER TABLE sales_opportunities ENABLE ROW LEVEL SECURITY;
ALTER TABLE sales_opportunities FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS sales_opportunities_tenant_policy ON sales_opportunities;
CREATE POLICY sales_opportunities_tenant_policy ON sales_opportunities
USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

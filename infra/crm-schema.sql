CREATE UNIQUE INDEX IF NOT EXISTS uq_organisations_tenant_id_id
    ON organisations(tenant_id, id);

CREATE TABLE IF NOT EXISTS crm_leads (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    organisation_id uuid NOT NULL,
    title text NOT NULL,
    status integer NOT NULL CHECK (status IN (1,2,3,4,5)),
    lead_source text NULL,
    original_partner_id uuid NULL,
    selling_partner_id uuid NULL,
    service_partner_id uuid NULL,
    account_owner_user_id uuid NULL,
    renewal_owner_user_id uuid NULL,
    unqualified_reason text NULL,
    CONSTRAINT fk_crm_lead_organisation_tenant
      FOREIGN KEY (tenant_id, organisation_id)
      REFERENCES organisations(tenant_id, id)
);

CREATE INDEX IF NOT EXISTS ix_crm_leads_tenant_status
    ON crm_leads(tenant_id, status);
CREATE INDEX IF NOT EXISTS ix_crm_leads_tenant_organisation
    ON crm_leads(tenant_id, organisation_id);

ALTER TABLE crm_leads ENABLE ROW LEVEL SECURITY;
ALTER TABLE crm_leads FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS crm_leads_tenant_policy ON crm_leads;
CREATE POLICY crm_leads_tenant_policy ON crm_leads
USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE crm_leads OWNER TO bos_owner;
GRANT SELECT, INSERT, UPDATE, DELETE ON crm_leads TO bos_app;

INSERT INTO crm_leads(
    id, tenant_id, organisation_id, title, status, lead_source)
VALUES
('44444444-4444-4444-4444-444444444441',
 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
 '11111111-1111-1111-1111-111111111111',
 'Alpha Lead', 1, 'Direct'),
('44444444-4444-4444-4444-444444444442',
 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
 '22222222-2222-2222-2222-222222222222',
 'Beta Lead', 1, 'Partner')
ON CONFLICT (id) DO NOTHING;

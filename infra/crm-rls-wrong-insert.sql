BEGIN;
SET LOCAL app.tenant_id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
INSERT INTO crm_leads(id, tenant_id, organisation_id, title, status)
VALUES ('55555555-5555-5555-5555-555555555555',
        'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
        '22222222-2222-2222-2222-222222222222',
        'Cross Tenant Lead', 1);
COMMIT;

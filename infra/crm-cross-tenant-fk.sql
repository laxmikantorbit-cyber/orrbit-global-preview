BEGIN;
INSERT INTO crm_leads(id, tenant_id, organisation_id, title, status)
VALUES ('66666666-6666-6666-6666-666666666666',
        'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
        '22222222-2222-2222-2222-222222222222',
        'Invalid Organisation Link', 1);
COMMIT;

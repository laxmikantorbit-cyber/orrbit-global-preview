SELECT CASE WHEN count(*)=0 THEN 'PASS:NO_CONTEXT' ELSE 'FAIL:NO_CONTEXT' END FROM catalog_products;
SET app.tenant_id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
SELECT CASE WHEN count(*)=1 AND min(code)='REPAIR' THEN 'PASS:TENANT_A' ELSE 'FAIL:TENANT_A' END FROM catalog_products;
RESET app.tenant_id;
SET app.tenant_id='bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
SELECT CASE WHEN count(*)=1 AND min(code)='SCHOOL' THEN 'PASS:TENANT_B' ELSE 'FAIL:TENANT_B' END FROM catalog_products;

SELECT CASE WHEN count(*) = 0 THEN 'PASS:NO_CONTEXT_READ'
  ELSE 'FAIL:NO_CONTEXT_READ' END
FROM crm_leads;

SET app.tenant_id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
SELECT CASE WHEN count(*) = 1 AND min(title) = 'Alpha Lead'
  THEN 'PASS:TENANT_A_ONLY' ELSE 'FAIL:TENANT_A_ONLY' END
FROM crm_leads;

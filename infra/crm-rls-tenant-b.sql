SET app.tenant_id = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
SELECT CASE WHEN count(*) = 1 AND min(title) = 'Beta Lead'
  THEN 'PASS:TENANT_B_ONLY' ELSE 'FAIL:TENANT_B_ONLY' END
FROM crm_leads;

SELECT CASE WHEN count(*)=0 THEN 'PASS:NO_CONTEXT_READ' ELSE 'FAIL:NO_CONTEXT_READ' END
FROM sales_opportunities;
SET app.tenant_id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
SELECT CASE WHEN count(*)=1 AND min(title)='Alpha Opportunity'
THEN 'PASS:TENANT_A_ONLY' ELSE 'FAIL:TENANT_A_ONLY' END
FROM sales_opportunities;

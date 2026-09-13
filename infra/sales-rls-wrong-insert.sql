BEGIN;
SET LOCAL app.tenant_id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
INSERT INTO sales_opportunities(
 id,tenant_id,organisation_id,title,stage,estimated_value,currency_code,probability_percent)
VALUES(
 '88888888-8888-8888-8888-888888888881','bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
 '22222222-2222-2222-2222-222222222222','Blocked RLS Insert',1,1000,'INR',10);
ROLLBACK;

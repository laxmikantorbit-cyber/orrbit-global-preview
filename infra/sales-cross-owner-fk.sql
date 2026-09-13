BEGIN;
INSERT INTO sales_opportunities(
 id,tenant_id,organisation_id,originating_lead_id,owner_user_id,title,stage,estimated_value,currency_code,probability_percent)
VALUES(
 '88888888-8888-8888-8888-888888888884','aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
 '11111111-1111-1111-1111-111111111111','44444444-4444-4444-4444-444444444441',
 '55555555-5555-5555-5555-555555555552','Bad Owner Link',1,1000,'INR',10);
ROLLBACK;

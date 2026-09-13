BEGIN;
INSERT INTO sales_opportunities(
 id,tenant_id,organisation_id,title,stage,estimated_value,currency_code,probability_percent)
VALUES(
 '88888888-8888-8888-8888-888888888882','aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
 '22222222-2222-2222-2222-222222222222','Bad Org Link',1,1000,'INR',10);
ROLLBACK;

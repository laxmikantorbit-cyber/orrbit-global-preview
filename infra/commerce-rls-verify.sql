SELECT CASE WHEN (SELECT count(*) FROM commerce_quotes)=0 AND (SELECT count(*) FROM commerce_orders)=0 AND (SELECT count(*) FROM commerce_subscriptions)=0 AND (SELECT count(*) FROM commerce_subscription_renewals)=0 THEN 'PASS:NO_CONTEXT' ELSE 'FAIL:NO_CONTEXT' END;
SET app.tenant_id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
SELECT CASE WHEN (SELECT count(*) FROM commerce_quotes)=1 AND (SELECT count(*) FROM commerce_orders)=1 AND (SELECT count(*) FROM commerce_subscriptions)=1 AND (SELECT count(*) FROM commerce_subscription_renewals)=1 THEN 'PASS:TENANT_A' ELSE 'FAIL:TENANT_A' END;
RESET app.tenant_id;
SET app.tenant_id='bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
SELECT CASE WHEN (SELECT count(*) FROM commerce_quotes)=1 AND (SELECT count(*) FROM commerce_orders)=1 AND (SELECT count(*) FROM commerce_subscriptions)=1 AND (SELECT count(*) FROM commerce_subscription_renewals)=1 THEN 'PASS:TENANT_B' ELSE 'FAIL:TENANT_B' END;

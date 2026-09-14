ALTER TABLE commerce_quotes OWNER TO bos_owner;
ALTER TABLE commerce_orders OWNER TO bos_owner;
ALTER TABLE commerce_subscriptions OWNER TO bos_owner;
GRANT SELECT, INSERT, UPDATE, DELETE ON commerce_quotes, commerce_orders, commerce_subscriptions TO bos_app;

INSERT INTO commerce_quotes(id,tenant_id,organisation_id,plan_id,plan_version_id,plan_version_number,amount,currency_code,billing_cycle,term_months,entitlement_snapshot,created_at_utc,valid_until_utc,status) VALUES
('81111111-1111-1111-1111-111111111111','aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','11111111-1111-1111-1111-111111111111','73333333-3333-3333-3333-333333333331','74444444-4444-4444-4444-444444444441',1,100,'INR',3,12,'{"desktop":1,"locations":1}',now(),now()+interval '30 days',2),
('81111111-1111-1111-1111-111111111112','bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb','22222222-2222-2222-2222-222222222222','73333333-3333-3333-3333-333333333332','74444444-4444-4444-4444-444444444442',1,200,'INR',3,12,'{"desktop":1,"locations":1}',now(),now()+interval '30 days',2)
ON CONFLICT (id) DO NOTHING;
INSERT INTO commerce_orders(id,tenant_id,organisation_id,quote_id,plan_id,plan_version_id,amount,currency_code,status,payment_id,paid_at_utc) VALUES
('82222222-2222-2222-2222-222222222221','aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','11111111-1111-1111-1111-111111111111','81111111-1111-1111-1111-111111111111','73333333-3333-3333-3333-333333333331','74444444-4444-4444-4444-444444444441',100,'INR',3,'pay-a','2026-09-14T00:00:00Z'),
('82222222-2222-2222-2222-222222222222','bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb','22222222-2222-2222-2222-222222222222','81111111-1111-1111-1111-111111111112','73333333-3333-3333-3333-333333333332','74444444-4444-4444-4444-444444444442',200,'INR',3,'pay-b','2026-09-14T00:00:00Z')
ON CONFLICT (id) DO NOTHING;

INSERT INTO commerce_subscriptions(id,tenant_id,organisation_id,order_id,plan_id,plan_version_id,starts_on,valid_until,entitlement_snapshot,status) VALUES
('83333333-3333-3333-3333-333333333331','aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','11111111-1111-1111-1111-111111111111','82222222-2222-2222-2222-222222222221','73333333-3333-3333-3333-333333333331','74444444-4444-4444-4444-444444444441','2026-09-14','2027-09-13','{"desktop":1,"locations":1}',1),
('83333333-3333-3333-3333-333333333332','bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb','22222222-2222-2222-2222-222222222222','82222222-2222-2222-2222-222222222222','73333333-3333-3333-3333-333333333332','74444444-4444-4444-4444-444444444442','2026-09-14','2027-09-13','{"desktop":1,"locations":1}',1)
ON CONFLICT (id) DO NOTHING;

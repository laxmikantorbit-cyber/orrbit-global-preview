ALTER TABLE catalog_products OWNER TO bos_owner;
ALTER TABLE catalog_plans OWNER TO bos_owner;
ALTER TABLE catalog_plan_versions OWNER TO bos_owner;
GRANT SELECT, INSERT, UPDATE, DELETE ON catalog_products, catalog_plans, catalog_plan_versions TO bos_app;

INSERT INTO catalog_products(id,tenant_id,code,name,status) VALUES
('71111111-1111-1111-1111-111111111111','aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','REPAIR','Repair',1),
('72222222-2222-2222-2222-222222222222','bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb','SCHOOL','School',1)
ON CONFLICT DO NOTHING;
INSERT INTO catalog_plans(id,tenant_id,product_id,code,name,status) VALUES
('73333333-3333-3333-3333-333333333331','aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','71111111-1111-1111-1111-111111111111','PRO','Pro',1),
('73333333-3333-3333-3333-333333333332','bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb','72222222-2222-2222-2222-222222222222','STD','Standard',1)
ON CONFLICT DO NOTHING;
INSERT INTO catalog_plan_versions(id,tenant_id,plan_id,version_number,amount,currency_code,billing_cycle,term_months,desktop_device_limit,location_limit,web_admin_seats,field_staff_seats,multi_location_cloud,features,effective_from_utc) VALUES
('74444444-4444-4444-4444-444444444441','aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','73333333-3333-3333-3333-333333333331',1,100,'INR',3,12,1,1,2,2,false,'["A"]',now()),
('74444444-4444-4444-4444-444444444442','bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb','73333333-3333-3333-3333-333333333332',1,200,'INR',3,12,1,1,2,2,false,'["B"]',now())
ON CONFLICT DO NOTHING;

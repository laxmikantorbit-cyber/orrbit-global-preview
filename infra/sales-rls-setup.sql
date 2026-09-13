INSERT INTO user_identities(id, subject, email, display_name, active) VALUES
('55555555-5555-5555-5555-555555555551','sales-user-a','sales-a@test.local','Sales A',true),
('55555555-5555-5555-5555-555555555552','sales-user-b','sales-b@test.local','Sales B',true)
ON CONFLICT (id) DO NOTHING;

INSERT INTO tenant_memberships(id,user_id,tenant_id,role_code,status) VALUES
('66666666-6666-6666-6666-666666666661','55555555-5555-5555-5555-555555555551','aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','SALES',2),
('66666666-6666-6666-6666-666666666662','55555555-5555-5555-5555-555555555552','bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb','SALES',2)
ON CONFLICT (user_id,tenant_id) DO NOTHING;

INSERT INTO sales_opportunities(
 id,tenant_id,organisation_id,originating_lead_id,owner_user_id,title,stage,
 estimated_value,currency_code,probability_percent,expected_close_date)
VALUES
('77777777-7777-7777-7777-777777777771','aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','11111111-1111-1111-1111-111111111111','44444444-4444-4444-4444-444444444441','55555555-5555-5555-5555-555555555551','Alpha Opportunity',1,30000,'INR',60,CURRENT_DATE + 30),
('77777777-7777-7777-7777-777777777772','bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb','22222222-2222-2222-2222-222222222222','44444444-4444-4444-4444-444444444442','55555555-5555-5555-5555-555555555552','Beta Opportunity',1,40000,'INR',70,CURRENT_DATE + 45)
ON CONFLICT (id) DO NOTHING;

ALTER TABLE sales_opportunities OWNER TO bos_owner;
GRANT SELECT, INSERT, UPDATE, DELETE ON sales_opportunities TO bos_app;

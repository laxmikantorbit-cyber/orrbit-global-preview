SET app.tenant_id = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
SELECT CASE WHEN count(*) = 1 AND min(name) = 'Beta Customer'
  THEN 'PASS:TENANT_B_ONLY' ELSE 'FAIL:TENANT_B_ONLY' END
FROM organisations;
SELECT CASE WHEN count(*) = 1 THEN 'PASS:TENANT_B_ROLE_ONLY' ELSE 'FAIL:TENANT_B_ROLE_ONLY' END FROM organisation_roles;

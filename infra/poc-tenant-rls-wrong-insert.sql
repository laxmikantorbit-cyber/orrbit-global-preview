BEGIN;
SET LOCAL app.tenant_id = 'TENANT-A';
INSERT INTO poc_customers (id, tenant_id, name, email)
VALUES ('33333333-3333-3333-3333-333333333333', 'TENANT-B', 'Cross Tenant', 'cross@example.test');
COMMIT;
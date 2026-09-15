ALTER TABLE tenants OWNER TO bos_owner;
ALTER TABLE user_identities OWNER TO bos_owner;
ALTER TABLE tenant_memberships OWNER TO bos_owner;

REVOKE ALL ON TABLE tenants FROM PUBLIC;
REVOKE ALL ON TABLE user_identities FROM PUBLIC;
REVOKE ALL ON TABLE tenant_memberships FROM PUBLIC;

GRANT SELECT ON tenants TO bos_app;
GRANT SELECT ON user_identities TO bos_app;
GRANT SELECT ON tenant_memberships TO bos_app;

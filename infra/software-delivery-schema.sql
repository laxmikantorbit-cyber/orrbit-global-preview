CREATE TABLE IF NOT EXISTS software_releases (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    product_code text NOT NULL CHECK (length(btrim(product_code)) > 0),
    version text NOT NULL CHECK (length(btrim(version)) > 0),
    channel text NOT NULL CHECK (length(btrim(channel)) > 0),
    platform text NOT NULL CHECK (length(btrim(platform)) > 0),
    architecture text NOT NULL CHECK (length(btrim(architecture)) > 0),
    file_name text NOT NULL CHECK (length(btrim(file_name)) > 0),
    download_url text NOT NULL CHECK (length(btrim(download_url)) > 0),
    sha256 text NOT NULL CHECK (sha256 ~ '^[0-9a-f]{64}$'),
    size_bytes bigint NULL CHECK (size_bytes IS NULL OR size_bytes >= 0),
    release_notes text NULL,
    published_at_utc timestamptz NOT NULL,
    active boolean NOT NULL DEFAULT true,
    created_at_utc timestamptz NOT NULL,
    UNIQUE (tenant_id, id),
    UNIQUE (tenant_id, product_code, version, channel, platform, architecture)
);

CREATE INDEX IF NOT EXISTS ix_software_releases_delivery
    ON software_releases(
        tenant_id, product_code, channel, platform, architecture,
        active, published_at_utc DESC);

ALTER TABLE software_releases ENABLE ROW LEVEL SECURITY;
ALTER TABLE software_releases FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS software_releases_tenant_policy ON software_releases;
CREATE POLICY software_releases_tenant_policy ON software_releases
    USING (
        tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    );

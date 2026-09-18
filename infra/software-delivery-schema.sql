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


DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_software_releases_product_code_safe') THEN
    ALTER TABLE software_releases
      ADD CONSTRAINT ck_software_releases_product_code_safe
      CHECK (product_code ~ '^[A-Z0-9_-]{1,64}$');
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_software_releases_channel_supported') THEN
    ALTER TABLE software_releases
      ADD CONSTRAINT ck_software_releases_channel_supported
      CHECK (channel IN ('Stable','Beta','Internal'));
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_software_releases_platform_supported') THEN
    ALTER TABLE software_releases
      ADD CONSTRAINT ck_software_releases_platform_supported
      CHECK (platform IN ('Windows'));
  END IF;
END $$;


DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_software_releases_architecture_supported') THEN
    ALTER TABLE software_releases
      ADD CONSTRAINT ck_software_releases_architecture_supported
      CHECK (architecture IN ('x64','x86','arm64'));
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_software_releases_version_safe') THEN
    ALTER TABLE software_releases
      ADD CONSTRAINT ck_software_releases_version_safe
      CHECK (length(version) <= 40 AND version ~ '[0-9]' AND version ~ '^[A-Za-z0-9][A-Za-z0-9.+-]*$');
  END IF;
END $$;


DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_software_releases_file_name_safe') THEN
    ALTER TABLE software_releases
      ADD CONSTRAINT ck_software_releases_file_name_safe
      CHECK (
        length(file_name) <= 160
        AND file_name !~ '[\\/]' AND file_name !~ '[<>:"|?*]'
        AND file_name !~ '\.\.' AND file_name ~* '\.(exe|msi|msix|zip)$'
      );
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_software_releases_notes_length') THEN
    ALTER TABLE software_releases
      ADD CONSTRAINT ck_software_releases_notes_length
      CHECK (release_notes IS NULL OR length(release_notes) <= 4000);
  END IF;
END $$;

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

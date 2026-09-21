CREATE TABLE IF NOT EXISTS secret_references (
  id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
  environment_name TEXT NOT NULL,
  secret_name TEXT NOT NULL,
  provider TEXT NOT NULL,
  provider_reference TEXT NOT NULL,
  reference_data JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(project_id, environment_name, secret_name)
);

CREATE INDEX IF NOT EXISTS idx_secret_references_project
  ON secret_references(project_id, environment_name, created_at DESC);

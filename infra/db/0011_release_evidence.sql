CREATE TABLE IF NOT EXISTS release_evidence_bundles (
  id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
  environment_name TEXT NOT NULL,
  status TEXT NOT NULL,
  evidence_data JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_release_evidence_project
  ON release_evidence_bundles(project_id, environment_name, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_release_evidence_status
  ON release_evidence_bundles(status, created_at DESC);

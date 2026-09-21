CREATE TABLE IF NOT EXISTS development_changes (
  id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
  status TEXT NOT NULL,
  change_data JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_development_changes_project
  ON development_changes(project_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_development_changes_status
  ON development_changes(status, created_at DESC);

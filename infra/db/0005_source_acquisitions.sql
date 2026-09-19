CREATE TABLE IF NOT EXISTS source_acquisitions (
  id UUID PRIMARY KEY,
  workspace_id UUID NOT NULL REFERENCES import_workspaces(id) ON DELETE CASCADE,
  project_id UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
  status TEXT NOT NULL,
  acquisition_data JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_source_acquisitions_workspace
  ON source_acquisitions(workspace_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_source_acquisitions_status
  ON source_acquisitions(status, created_at DESC);

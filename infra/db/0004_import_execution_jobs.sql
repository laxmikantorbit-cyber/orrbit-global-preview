CREATE TABLE IF NOT EXISTS import_execution_jobs (
  id UUID PRIMARY KEY,
  workspace_id UUID NOT NULL REFERENCES import_workspaces(id) ON DELETE CASCADE,
  project_id UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
  status TEXT NOT NULL,
  execution_data JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_import_execution_jobs_workspace
  ON import_execution_jobs(workspace_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_import_execution_jobs_status
  ON import_execution_jobs(status, created_at DESC);

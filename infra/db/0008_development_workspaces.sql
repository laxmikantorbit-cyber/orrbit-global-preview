CREATE TABLE IF NOT EXISTS development_workspaces (
  id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
  status TEXT NOT NULL,
  workspace_data JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_development_workspaces_project
  ON development_workspaces(project_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_development_workspaces_status
  ON development_workspaces(status, created_at DESC);

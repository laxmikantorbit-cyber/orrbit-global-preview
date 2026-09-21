CREATE TABLE IF NOT EXISTS source_build_jobs (
  id UUID PRIMARY KEY,
  acquisition_id UUID NOT NULL REFERENCES source_acquisitions(id) ON DELETE CASCADE,
  workspace_id UUID NOT NULL REFERENCES import_workspaces(id) ON DELETE CASCADE,
  project_id UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
  status TEXT NOT NULL,
  build_data JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_source_build_jobs_acquisition
  ON source_build_jobs(acquisition_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_source_build_jobs_status
  ON source_build_jobs(status, created_at DESC);

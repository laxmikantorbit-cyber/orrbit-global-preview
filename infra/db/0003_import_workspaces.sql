CREATE TABLE IF NOT EXISTS import_workspaces (
  id UUID PRIMARY KEY,
  import_plan_id UUID NOT NULL REFERENCES import_plans(id) ON DELETE CASCADE,
  project_id UUID REFERENCES projects(id) ON DELETE SET NULL,
  status TEXT NOT NULL,
  source_reference_status TEXT NOT NULL,
  route_capture JSONB NOT NULL DEFAULT '[]'::jsonb,
  module_capture JSONB NOT NULL DEFAULT '[]'::jsonb,
  capture_checklist JSONB NOT NULL DEFAULT '[]'::jsonb,
  deploy_gate JSONB NOT NULL DEFAULT '{}'::jsonb,
  workspace_data JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_import_workspaces_plan ON import_workspaces(import_plan_id);
CREATE INDEX IF NOT EXISTS idx_import_workspaces_project ON import_workspaces(project_id);
CREATE INDEX IF NOT EXISTS idx_import_workspaces_status ON import_workspaces(status, created_at DESC);

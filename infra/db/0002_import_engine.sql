CREATE TABLE IF NOT EXISTS import_plans (
  id UUID PRIMARY KEY,
  source_type TEXT NOT NULL,
  source_ref TEXT NOT NULL,
  requested_project_name TEXT NOT NULL,
  project_type TEXT NOT NULL,
  target_environment TEXT NOT NULL DEFAULT 'development',
  risk_level TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'plan_ready',
  route_inventory JSONB NOT NULL DEFAULT '[]'::jsonb,
  manifest_draft JSONB NOT NULL DEFAULT '{}'::jsonb,
  plan_data JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_import_plans_created ON import_plans(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_import_plans_source ON import_plans(source_type, created_at DESC);

ALTER TABLE jobs DROP CONSTRAINT IF EXISTS jobs_plan_id_fkey;

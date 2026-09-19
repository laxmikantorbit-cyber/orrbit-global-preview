ALTER TABLE projects
  ADD COLUMN IF NOT EXISTS source_mode TEXT NOT NULL DEFAULT 'new-project';

CREATE TABLE IF NOT EXISTS provisioning_plans (
  id UUID PRIMARY KEY,
  prompt TEXT NOT NULL,
  plan_data JSONB NOT NULL,
  status TEXT NOT NULL DEFAULT 'pending',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  approved_at TIMESTAMPTZ
);

ALTER TABLE jobs
  ADD COLUMN IF NOT EXISTS plan_id UUID REFERENCES provisioning_plans(id) ON DELETE SET NULL;

ALTER TABLE jobs
  ADD COLUMN IF NOT EXISTS evidence JSONB NOT NULL DEFAULT '[]'::jsonb;

CREATE INDEX IF NOT EXISTS idx_plans_created
  ON provisioning_plans(created_at DESC);

CREATE INDEX IF NOT EXISTS idx_jobs_plan
  ON jobs(plan_id);

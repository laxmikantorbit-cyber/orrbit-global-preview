CREATE TABLE projects (
  id UUID PRIMARY KEY,
  name TEXT NOT NULL,
  project_type TEXT NOT NULL,
  source_mode TEXT NOT NULL DEFAULT 'new-project',
  lifecycle_status TEXT NOT NULL DEFAULT 'development',
  repository_full_name TEXT,
  default_branch TEXT NOT NULL DEFAULT 'main',
  production_protected BOOLEAN NOT NULL DEFAULT TRUE,
  health_path TEXT NOT NULL DEFAULT '/api/health',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE project_environments (
  id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
  environment_name TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'unconfigured',
  frontend_provider TEXT,
  backend_provider TEXT,
  database_provider TEXT,
  region TEXT,
  UNIQUE(project_id, environment_name)
);

CREATE TABLE provisioning_plans (
  id UUID PRIMARY KEY,
  prompt TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'draft',
  plan_data JSONB NOT NULL,
  approved_at TIMESTAMPTZ,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE jobs (
  id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
  job_type TEXT NOT NULL,
  state TEXT NOT NULL,
  risk_level TEXT NOT NULL,
  requested_by TEXT NOT NULL,
  request_summary TEXT NOT NULL,
  current_stage TEXT,
  plan_id UUID,
  evidence JSONB NOT NULL DEFAULT '[]'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE approvals (
  id UUID PRIMARY KEY,
  job_id UUID NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
  approval_type TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'pending',
  requested_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  decided_at TIMESTAMPTZ,
  decided_by TEXT,
  decision_note TEXT
);

CREATE TABLE releases (
  id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
  environment_name TEXT NOT NULL,
  source_revision TEXT NOT NULL,
  provider_deployment_id TEXT,
  health_verified BOOLEAN NOT NULL DEFAULT FALSE,
  released_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE audit_events (
  id UUID PRIMARY KEY,
  project_id UUID REFERENCES projects(id) ON DELETE SET NULL,
  actor TEXT NOT NULL,
  event_type TEXT NOT NULL,
  event_data JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_projects_created ON projects(created_at DESC);
CREATE INDEX idx_environments_project ON project_environments(project_id, environment_name);
CREATE INDEX idx_plans_created ON provisioning_plans(created_at DESC);
CREATE INDEX idx_jobs_project_created ON jobs(project_id, created_at DESC);
CREATE INDEX idx_jobs_plan ON jobs(plan_id);
CREATE INDEX idx_releases_project_env ON releases(project_id, environment_name, released_at DESC);
CREATE INDEX idx_audit_project_created ON audit_events(project_id, created_at DESC);


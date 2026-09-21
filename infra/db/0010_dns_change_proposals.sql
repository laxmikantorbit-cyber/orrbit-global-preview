CREATE TABLE IF NOT EXISTS dns_change_proposals (
  id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
  status TEXT NOT NULL,
  proposal_data JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_dns_change_proposals_project
  ON dns_change_proposals(project_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_dns_change_proposals_status
  ON dns_change_proposals(status, created_at DESC);

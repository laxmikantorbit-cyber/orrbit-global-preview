CREATE TABLE IF NOT EXISTS owner_accounts (
  id UUID PRIMARY KEY,
  email TEXT NOT NULL UNIQUE,
  password_hash TEXT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS owner_sessions (
  id UUID PRIMARY KEY,
  owner_id UUID NOT NULL REFERENCES owner_accounts(id) ON DELETE CASCADE,
  token_hash TEXT NOT NULL UNIQUE,
  expires_at TIMESTAMPTZ NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  last_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_owner_sessions_owner
  ON owner_sessions(owner_id, expires_at DESC);
CREATE INDEX IF NOT EXISTS idx_owner_sessions_token
  ON owner_sessions(token_hash);

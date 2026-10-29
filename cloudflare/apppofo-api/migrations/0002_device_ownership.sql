-- S5-04B: additive ownership. Existing admin-created profiles remain unowned.
-- Do not assign legacy rows to the first device that signs in.
CREATE TABLE device_identities (
  id TEXT PRIMARY KEY NOT NULL,
  public_key_jwk TEXT NOT NULL,
  key_thumbprint TEXT NOT NULL UNIQUE,
  created_at INTEGER NOT NULL,
  revoked_at INTEGER
);
CREATE TABLE device_challenges (
  id TEXT PRIMARY KEY NOT NULL,
  purpose TEXT NOT NULL CHECK (purpose IN ('register','session')),
  subject TEXT NOT NULL,
  expires_at INTEGER NOT NULL
);
CREATE INDEX idx_device_challenges_subject ON device_challenges(purpose,subject,expires_at);
CREATE TABLE device_sessions (
  token_hash TEXT PRIMARY KEY NOT NULL,
  device_id TEXT NOT NULL REFERENCES device_identities(id) ON DELETE CASCADE,
  expires_at INTEGER NOT NULL,
  revoked_at INTEGER
);
CREATE INDEX idx_device_sessions_device ON device_sessions(device_id,expires_at);
ALTER TABLE profiles ADD COLUMN owner_device_id TEXT REFERENCES device_identities(id);
CREATE INDEX idx_profiles_owner ON profiles(owner_device_id,created_at DESC,id DESC);

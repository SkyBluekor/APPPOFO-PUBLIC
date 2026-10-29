-- Additive, apply only to a separately approved staging database first.
-- Separate consent generation invalidates pending enrollment without changing AES-GCM AAD.
ALTER TABLE face_consent_state ADD COLUMN consent_generation INTEGER NOT NULL DEFAULT 0 CHECK(consent_generation>=0);
CREATE TABLE profile_create_receipts (
  actor_scope TEXT NOT NULL,
  request_id TEXT NOT NULL,
  payload_digest TEXT NOT NULL,
  profile_id TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  PRIMARY KEY(actor_scope, request_id)
);
-- No cascade: deleting a profile must not make an old creation key create it again.
CREATE TABLE face_scanner_authorizations (
  device_id TEXT PRIMARY KEY REFERENCES device_identities(id) ON DELETE CASCADE,
  authorized_at INTEGER NOT NULL,
  revoked_at INTEGER
);
CREATE TABLE face_mutation_rates (
  actor_scope TEXT PRIMARY KEY,
  window_started_at INTEGER NOT NULL,
  request_count INTEGER NOT NULL CHECK(request_count >= 0)
);
CREATE TABLE face_discovery_audit (
  event_id TEXT PRIMARY KEY,
  profile_id TEXT REFERENCES profiles(id) ON DELETE SET NULL,
  actor_scope TEXT NOT NULL,
  revision INTEGER NOT NULL,
  at INTEGER NOT NULL
);

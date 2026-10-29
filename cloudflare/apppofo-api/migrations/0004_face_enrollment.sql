-- S5-08-B: Additive encrypted face enrollment schema.
-- No biometric images or plaintext embeddings. No change to existing profiles/items.
-- Local test DB only until separate explicit production approval.
CREATE TABLE face_system_state (
  singleton INTEGER PRIMARY KEY CHECK (singleton=1),
  purge_epoch INTEGER NOT NULL DEFAULT 0 CHECK (purge_epoch>=0)
);
INSERT INTO face_system_state(singleton,purge_epoch) VALUES(1,0);

CREATE TABLE face_consent_state (
  profile_id TEXT PRIMARY KEY NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
  revision INTEGER NOT NULL DEFAULT 0 CHECK (revision>=0),
  purge_epoch INTEGER NOT NULL DEFAULT 0 CHECK (purge_epoch>=0),
  policy_version TEXT,
  storage_consent INTEGER NOT NULL DEFAULT 0 CHECK (storage_consent IN (0,1)),
  discoverable INTEGER NOT NULL DEFAULT 0 CHECK (discoverable IN (0,1)),
  confirmed_at INTEGER,
  withdrawn_at INTEGER,
  last_request_id TEXT,
  last_actor_scope TEXT,
  last_operation TEXT CHECK (last_operation IN ('ENROLL','DELETE') OR last_operation IS NULL),
  updated_at INTEGER,
  CHECK (discoverable=0 OR storage_consent=1)
);
CREATE INDEX idx_face_consent_discoverable ON face_consent_state(storage_consent,discoverable);

CREATE TABLE face_templates (
  profile_id TEXT PRIMARY KEY NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
  model_version TEXT NOT NULL,
  cipher_suite TEXT NOT NULL CHECK (cipher_suite='AES-256-GCM'),
  key_version INTEGER NOT NULL CHECK (key_version>=1),
  nonce BLOB NOT NULL CHECK (length(nonce)=12),
  ciphertext BLOB NOT NULL CHECK (length(ciphertext)=1552),
  revision INTEGER NOT NULL CHECK (revision>=1),
  enrolled_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL
);

CREATE TABLE face_idempotency (
  actor_scope TEXT NOT NULL,
  operation TEXT NOT NULL CHECK (operation IN ('ENROLL','DELETE')),
  request_id TEXT NOT NULL,
  target_uuid TEXT NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
  payload_digest TEXT NOT NULL,
  result_revision INTEGER NOT NULL CHECK (result_revision>=1),
  purge_epoch INTEGER NOT NULL CHECK (purge_epoch>=0),
  expires_at INTEGER NOT NULL,
  created_at INTEGER NOT NULL,
  PRIMARY KEY(actor_scope,operation,request_id)
);
CREATE INDEX idx_face_idempotency_expiry ON face_idempotency(expires_at);

CREATE TABLE face_consent_audit (
  event_id TEXT PRIMARY KEY NOT NULL,
  profile_id TEXT REFERENCES profiles(id) ON DELETE SET NULL,
  action TEXT NOT NULL CHECK (action IN ('ENROLL','DELETE','PURGE')),
  actor_scope TEXT NOT NULL,
  policy_version TEXT,
  revision INTEGER,
  at INTEGER NOT NULL
);
CREATE INDEX idx_face_audit_profile ON face_consent_audit(profile_id,at);

-- This is a transaction-local assertion row, inserted after conditional CAS UPDATE
-- and deleted before DB.batch commits. A failed CHECK aborts and rolls back the batch.
-- Prevents ignored zero-row UPDATE from committing a subsequent template write.
CREATE TABLE face_operation_guards (
  request_id TEXT PRIMARY KEY,
  assertion INTEGER NOT NULL CHECK (assertion=1)
);

CREATE TABLE face_admin_operations (
  operation_id TEXT PRIMARY KEY,
  token_hash TEXT NOT NULL UNIQUE,
  expires_at INTEGER NOT NULL,
  used_at INTEGER,
  purge_epoch INTEGER NOT NULL,
  expected_count INTEGER NOT NULL CHECK (expected_count>=0),
  deleted_count INTEGER,
  created_at INTEGER NOT NULL
);

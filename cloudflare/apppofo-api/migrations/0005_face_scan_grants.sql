-- S5-09-A local-only face scanning permissions and bounded per-device search.
-- No source images, query embeddings, scores or scanned identities are persisted.
-- Does not change existing face templates/profiles or enable production matching.
CREATE TABLE face_scan_grants (
  device_id TEXT PRIMARY KEY NOT NULL
    REFERENCES device_identities(id) ON DELETE CASCADE,
  policy_version TEXT NOT NULL,
  consented_at INTEGER NOT NULL,
  revoked_at INTEGER,
  window_started_at INTEGER NOT NULL DEFAULT 0,
  request_count INTEGER NOT NULL DEFAULT 0 CHECK (request_count>=0)
);

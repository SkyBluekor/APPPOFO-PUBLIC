-- S5-07: multiple independently authenticated devices can manage one profile.
CREATE TABLE profile_devices (
 profile_id TEXT NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
 device_id TEXT NOT NULL REFERENCES device_identities(id) ON DELETE CASCADE,
 granted_at INTEGER NOT NULL,
 revoked_at INTEGER,
 PRIMARY KEY(profile_id,device_id)
);
CREATE INDEX idx_profile_devices_device ON profile_devices(device_id,revoked_at,profile_id);
-- Preserve ownership for existing Android users. Unowned admin rows remain unowned.
INSERT INTO profile_devices(profile_id,device_id,granted_at)
 SELECT p.id,p.owner_device_id,p.created_at FROM profiles p
 JOIN device_identities d ON d.id=p.owner_device_id WHERE p.owner_device_id IS NOT NULL;

CREATE TABLE profile_link_credentials (
 profile_id TEXT PRIMARY KEY REFERENCES profiles(id) ON DELETE CASCADE,
 code_hash TEXT NOT NULL UNIQUE,
 rotated_at INTEGER NOT NULL
);
CREATE TABLE profile_link_attempts (
 device_id TEXT PRIMARY KEY REFERENCES device_identities(id) ON DELETE CASCADE,
 window_start INTEGER NOT NULL,
 attempts INTEGER NOT NULL CHECK(attempts >= 0)
);

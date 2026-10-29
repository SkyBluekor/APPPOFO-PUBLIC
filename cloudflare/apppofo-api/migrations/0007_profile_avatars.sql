-- Profile display images are separate from encrypted recognition templates.
-- Only a bounded, re-encoded JPEG thumbnail is accepted by the avatar endpoint.
CREATE TABLE IF NOT EXISTS profile_avatars (
  profile_id TEXT PRIMARY KEY REFERENCES profiles(id) ON DELETE CASCADE,
  image_base64 TEXT NOT NULL CHECK(length(image_base64) BETWEEN 4 AND 32768),
  updated_at INTEGER NOT NULL
);

-- Cloud profile-only storage. Local SQLite IDs, avatars, photographs and face embeddings
-- are deliberately not copied. Cloud IDs are independent random UUID strings.
CREATE TABLE IF NOT EXISTS profiles (
    id TEXT PRIMARY KEY NOT NULL,
    display_name TEXT NOT NULL CHECK (length(trim(display_name)) BETWEEN 1 AND 100),
    headline TEXT NOT NULL DEFAULT '',
    bio TEXT NOT NULL DEFAULT '',
    email TEXT NOT NULL DEFAULT '',
    website TEXT NOT NULL DEFAULT '',
    business_card_theme TEXT NOT NULL DEFAULT 'AURORA'
        CHECK (business_card_theme IN ('AURORA', 'BLUSH', 'NEUTRAL')),
    is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
    created_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL CHECK (updated_at >= created_at)
);

CREATE TABLE IF NOT EXISTS profile_items (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    profile_id TEXT NOT NULL,
    item_type TEXT NOT NULL CHECK (item_type IN ('SKILL', 'PROJECT', 'LINK')),
    title TEXT NOT NULL CHECK (length(trim(title)) > 0),
    description TEXT NOT NULL DEFAULT '',
    url TEXT NOT NULL DEFAULT '',
    sort_order INTEGER NOT NULL DEFAULT 0 CHECK (sort_order >= 0),
    FOREIGN KEY (profile_id) REFERENCES profiles(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_profiles_created ON profiles(created_at DESC, id);
CREATE INDEX IF NOT EXISTS idx_items_profile_sort ON profile_items(profile_id, sort_order, id);
CREATE INDEX IF NOT EXISTS idx_items_type_title ON profile_items(item_type, title, profile_id);

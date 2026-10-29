function itemInserts(db, profileId, items) {
  return items.map((item) => db.prepare(
    `INSERT INTO profile_items (profile_id, item_type, title, description, url, sort_order)
     VALUES (?, ?, ?, ?, ?, ?)`
  ).bind(profileId, item.item_type, item.title, item.description, item.url, item.sort_order));
}

export async function listProfiles(db, { limit, offset }) {
  const [count, page] = await db.batch([
    db.prepare("SELECT COUNT(*) AS total FROM profiles"),
    db.prepare(`SELECT id, display_name, headline, business_card_theme, updated_at,
                  CASE WHEN owner_device_id IS NULL THEN 'ADMIN' ELSE 'DEVICE' END AS ownership
                FROM profiles ORDER BY created_at DESC, id DESC LIMIT ? OFFSET ?`)
      .bind(limit, offset),
  ]);
  return { data: page.results, total: count.results[0].total, limit, offset };
}

export async function createProfile(db, profile, options = {}) {
  const id = options.id ?? crypto.randomUUID();
  const now = options.now ?? Date.now();
  await db.batch([
    db.prepare(
      `INSERT INTO profiles (
         id, display_name, headline, bio, email, website, business_card_theme,
         is_active, created_at, updated_at
       ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
    ).bind(id, profile.display_name, profile.headline, profile.bio,
      profile.email, profile.website, profile.business_card_theme, profile.is_active, now, now),
    ...itemInserts(db, id, profile.items),
    ...(options.receipt ? [options.receipt] : []),
  ]);
  return { id, created_at: now, updated_at: now };
}

export async function updateProfile(db, id, profile) {
  // This is explicitly a full replacement of the cloud copy, never of local SQLite.
  const exists = await db.prepare("SELECT id FROM profiles WHERE id = ?").bind(id).first();
  if (!exists) return null;
  const now = Date.now();
  await db.batch([
    db.prepare(
      `UPDATE profiles SET display_name = ?, headline = ?, bio = ?, email = ?, website = ?,
         business_card_theme = ?, is_active = ?, updated_at = ? WHERE id = ?`
    ).bind(profile.display_name, profile.headline, profile.bio, profile.email,
      profile.website, profile.business_card_theme, profile.is_active, now, id),
    db.prepare("DELETE FROM profile_items WHERE profile_id = ?").bind(id),
    ...itemInserts(db, id, profile.items),
  ]);
  return { id, updated_at: now };
}

export async function deleteProfile(db, id) {
  const result = await db.prepare("DELETE FROM profiles WHERE id = ?").bind(id).run();
  // D1 includes rows removed by ON DELETE CASCADE in the change count.
  return (result.meta?.changes ?? 0) > 0;
}

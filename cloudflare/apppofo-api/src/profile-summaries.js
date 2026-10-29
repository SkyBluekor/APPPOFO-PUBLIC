import { validCloudId } from './validation.js';

const short = (value, max) => typeof value === 'string' ? Array.from(value.trim()).slice(0, max).join('') : '';
function webUrl(value) {
  if (typeof value !== 'string' || value.length > 512) return null;
  try {
    const url = new URL(value);
    return ['https:', 'http:'].includes(url.protocol) && !url.username && !url.password && url.href.length <= 512 ? url.href : null;
  } catch { return null; }
}

// Public recognition includes the concise conference profile without another switch.
// One snapshot checks public face eligibility and reads the allowed summary fields.
// Never query email, website, templates, scores or private device information.
export async function readPublicSummary(db, id, model) {
  if (!validCloudId(id)) return null;
  const row = await db.prepare(`SELECT p.id,p.display_name,p.headline,p.bio,a.image_base64 AS avatar_base64,
    COALESCE((
      SELECT json_group_array(json_object('item_type',item_type,'title',title,'description',description,'url',url))
      FROM (SELECT item_type,title,description,url FROM profile_items WHERE profile_id=p.id ORDER BY sort_order,id LIMIT 45)
    ),'[]') AS items_json
    FROM profiles p JOIN face_consent_state c ON c.profile_id=p.id
    JOIN face_templates t ON t.profile_id=p.id JOIN face_system_state f ON f.singleton=1
    LEFT JOIN profile_avatars a ON a.profile_id=p.id
    WHERE p.id=? AND p.is_active=1 AND c.storage_consent=1 AND c.discoverable=1
      AND c.revision=t.revision AND c.purge_epoch=f.purge_epoch AND t.model_version=?`)
    .bind(id, model).first();
  if (!row) return null;
  const items = JSON.parse(row.items_json);
  const skills = items.filter(i => i.item_type === 'SKILL').slice(0, 5).map(i => short(i.title, 32));
  const featured = items.find(i => i.item_type === 'PROJECT');
  const project = featured ? {title: short(featured.title, 80), description: short(featured.description, 140), url: webUrl(featured.url)} : null;
  const links = items.filter(i => i.item_type === 'LINK').map(i => ({title: short(i.title, 32), url: webUrl(i.url)})).filter(i => i.url).slice(0, 2);
  return {id: row.id, display_name: row.display_name, headline: row.headline, avatar_base64: row.avatar_base64,
    bio: short(row.bio, 140), skills, project, links};
}

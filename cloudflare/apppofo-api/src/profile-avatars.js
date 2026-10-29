import { json, problem, readJson } from './http.js';
import { validCloudId } from './validation.js';

// No originals, EXIF or arbitrary media. The client re-encodes a <=192px JPEG.
export function validAvatarImage(value) {
  if (typeof value !== 'string' || value.length > 32768 || value.length % 4 ||
      !/^[A-Za-z0-9+/]+={0,2}$/.test(value)) return false;
  let bytes; try { bytes = Uint8Array.from(atob(value), c => c.charCodeAt(0)); } catch { return false; }
  if (bytes.length < 16 || bytes[0] !== 255 || bytes[1] !== 216 ||
      bytes.at(-2) !== 255 || bytes.at(-1) !== 217) return false;
  let dimensions = false;
  for (let i = 2; i + 3 < bytes.length;) {
    if (bytes[i++] !== 255) return false;
    while (bytes[i] === 255) i++;
    const marker = bytes[i++];
    if (marker === 218) return dimensions;
    const length = (bytes[i] << 8) | bytes[i + 1];
    if (length < 2 || i + length > bytes.length) return false;
    if (marker >= 225 && marker <= 239 || marker === 254) return false; // no EXIF or comments
    if (marker === 224 && (length !== 16 || String.fromCharCode(...bytes.slice(i + 2, i + 7)) !== 'JFIF\0' || bytes[i + 14] || bytes[i + 15])) return false;
    if ([192, 193, 194].includes(marker)) {
      if (length < 8) return false;
      const height = (bytes[i + 3] << 8) | bytes[i + 4], width = (bytes[i + 5] << 8) | bytes[i + 6];
      if (!width || !height || width > 192 || height > 192 || bytes[i + 2] !== 8) return false;
      dimensions = true;
    }
    i += length;
  }
  return false;
}

export async function attachProfileAvatars(db, profiles) {
  if (!profiles.length) return profiles;
  const ids = profiles.map(p => p.id);
  const rows = (await db.prepare(`SELECT profile_id,image_base64 FROM profile_avatars WHERE profile_id IN (${ids.map(() => '?').join(',')})`).bind(...ids).all()).results;
  return profiles.map(p => ({...p, avatar_base64: rows.find(a => a.profile_id === p.id)?.image_base64 ?? null}));
}
export async function profileResponseWithAvatar(db, response) {
  if (response.status !== 200) return response;
  const body = await response.json();
  body.data = (await attachProfileAvatars(db, [body.data]))[0];
  return json(body);
}
export async function saveProfileAvatar(request, db, id, owner = null) {
  if (!validCloudId(id)) return problem(400, 'INVALID_ID', '프로필 ID를 확인해 주세요.');
  if (request.method !== 'PUT') return problem(405, 'METHOD_NOT_ALLOWED', '지원하지 않는 메서드입니다.');
  const access = owner === null ? '1=1' : 'EXISTS(SELECT 1 FROM profile_devices d WHERE d.profile_id=profiles.id AND d.device_id=? AND d.revoked_at IS NULL)';
  const args = owner === null ? [id] : [id, owner];
  if (!await db.prepare(`SELECT id FROM profiles WHERE id=? AND ${access}`).bind(...args).first())
    return problem(404, 'NOT_FOUND', '프로필이 없습니다.');
  const received = await readJson(request); if (received.error) return received.error;
  const body = received.value;
  if (!body || Object.keys(body).sort().join(',') !== 'image_base64,only_if_missing' ||
      typeof body.only_if_missing !== 'boolean' || !validAvatarImage(body.image_base64))
    return problem(400, 'INVALID_AVATAR', '192px 이하의 프로필 JPEG 이미지가 필요합니다.');
  const conflict = body.only_if_missing ? 'DO NOTHING' : 'DO UPDATE SET image_base64=excluded.image_base64,updated_at=excluded.updated_at';
  const result = await db.prepare(`INSERT INTO profile_avatars(profile_id,image_base64,updated_at) SELECT id,?,? FROM profiles WHERE id=? AND ${access} ON CONFLICT(profile_id) ${conflict}`)
    .bind(body.image_base64, Date.now(), ...args).run();
  if (!result.meta?.changes && !await db.prepare(`SELECT id FROM profiles WHERE id=? AND ${access}`).bind(...args).first())
    return problem(404, 'NOT_FOUND', '프로필이 없습니다.');
  return json({data:{id, saved:result.meta?.changes > 0}});
}

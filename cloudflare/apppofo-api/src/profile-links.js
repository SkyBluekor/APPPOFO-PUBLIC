import { json, problem, readJson } from "./http.js";
import { validCloudId } from "./validation.js";

const encoder=new TextEncoder();
const CODE_PATTERN=/^applink_[A-Za-z0-9_-]{32}$/;
const WINDOW_MS=15*60*1000, MAX_ATTEMPTS=5;

function makeCode() {
  const bytes=crypto.getRandomValues(new Uint8Array(24));
  let binary="";
  for(const byte of bytes)binary+=String.fromCharCode(byte);
  return "applink_"+btoa(binary).replace(/\+/g,"-").replace(/\//g,"_").replace(/=+$/,"");
}
async function hashCode(code) {
  const digest=new Uint8Array(await crypto.subtle.digest("SHA-256",encoder.encode("AppPofo-profile-link-v1\n"+code)));
  let binary="";
  for(const byte of digest)binary+=String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g,"-").replace(/\//g,"_").replace(/=+$/,"");
}
export async function issueProfileCode(db,id,owner=null) {
  if(!validCloudId(id))return problem(400,"INVALID_ID","프로필 ID가 올바르지 않습니다.");
  const lookup=owner===null
    ?db.prepare("SELECT id FROM profiles WHERE id=?").bind(id)
    :db.prepare("SELECT p.id FROM profiles p JOIN profile_devices d ON d.profile_id=p.id WHERE p.id=? AND d.device_id=? AND d.revoked_at IS NULL").bind(id,owner);
  if(!await lookup.first())return problem(404,"NOT_FOUND","프로필이 없습니다.");
  const code=makeCode(),hash=await hashCode(code),now=Date.now();
  // Recheck ownership when writing: revocation cannot be bypassed by a stale read.
  const result=await db.prepare("INSERT INTO profile_link_credentials(profile_id,code_hash,rotated_at) SELECT ?,?,? WHERE EXISTS(SELECT 1 FROM profiles p WHERE p.id=? AND (?=1 OR EXISTS(SELECT 1 FROM profile_devices d WHERE d.profile_id=p.id AND d.device_id=? AND d.revoked_at IS NULL))) ON CONFLICT(profile_id) DO UPDATE SET code_hash=excluded.code_hash,rotated_at=excluded.rotated_at")
    .bind(id,hash,now,id,owner===null?1:0,owner).run();
  if((result.meta?.changes??0)===0)return problem(404,"NOT_FOUND","프로필이 없습니다.");
  return json({data:{profile_id:id,link_code:code}});
}
export async function connectProfileByCode(request,db,deviceId) {
  const parsed=await readJson(request);
  if(parsed.error)return parsed.error;
  const body=parsed.value;
  if(!body||typeof body!=="object"||Array.isArray(body)||
      Object.keys(body).length!==1||typeof body.code!=="string"||!CODE_PATTERN.test(body.code))
    return problem(400,"INVALID_INPUT","연결 코드 형식을 확인해 주세요.");
  const now=Date.now();
  await db.prepare("INSERT INTO profile_link_attempts(device_id,window_start,attempts) VALUES(?,?,1) ON CONFLICT(device_id) DO UPDATE SET attempts=CASE WHEN window_start<? THEN 1 ELSE attempts+1 END, window_start=CASE WHEN window_start<? THEN excluded.window_start ELSE window_start END")
    .bind(deviceId,now,now-WINDOW_MS,now-WINDOW_MS).run();
  const state=await db.prepare("SELECT attempts FROM profile_link_attempts WHERE device_id=?").bind(deviceId).first();
  if(!state||state.attempts>MAX_ATTEMPTS)
    return problem(429,"TOO_MANY_ATTEMPTS","연결 시도가 많습니다. 잠시 후 다시 시도해 주세요.");
  const hash=await hashCode(body.code);
  const matched=await db.prepare("SELECT profile_id FROM profile_link_credentials WHERE code_hash=?").bind(hash).first();
  if(!matched)return problem(404,"NOT_FOUND","연결 코드를 확인해 주세요.");
  // Recheck current code hash during insert to reject already rotated codes.
  const result=await db.prepare("INSERT INTO profile_devices(profile_id,device_id,granted_at,revoked_at) SELECT ?,?,?,NULL WHERE EXISTS(SELECT 1 FROM profile_link_credentials WHERE profile_id=? AND code_hash=?) ON CONFLICT(profile_id,device_id) DO UPDATE SET granted_at=excluded.granted_at,revoked_at=NULL")
    .bind(matched.profile_id,deviceId,now,matched.profile_id,hash).run();
  if((result.meta?.changes??0)===0)
    return problem(404,"NOT_FOUND","연결 코드를 확인해 주세요.");
  return json({data:{profile_id:matched.profile_id}});
}


// Lists only devices approved to edit this profile, never their public keys or tokens.
export async function listProfileDevices(db,id,owner) {
  if(!validCloudId(id))return problem(400,"INVALID_ID","프로필 ID가 올바르지 않습니다.");
  const access=await db.prepare("SELECT 1 FROM profile_devices WHERE profile_id=? AND device_id=? AND revoked_at IS NULL").bind(id,owner).first();
  if(!access)return problem(404,"NOT_FOUND","프로필이 없습니다.");
  const rows=await db.prepare("SELECT device_id,granted_at FROM profile_devices WHERE profile_id=? AND revoked_at IS NULL ORDER BY granted_at,device_id").bind(id).all();
  return json({data:rows.results});
}

export async function revokeProfileDevice(db,id,target,owner) {
  if(!validCloudId(id)||!validCloudId(target))
    return problem(400,"INVALID_ID","기기 또는 프로필 ID가 올바르지 않습니다.");
  const access=await db.prepare("SELECT 1 FROM profile_devices WHERE profile_id=? AND device_id=? AND revoked_at IS NULL").bind(id,owner).first();
  if(!access)return problem(404,"NOT_FOUND","프로필이 없습니다.");
  const count=await db.prepare("SELECT COUNT(*) AS count FROM profile_devices WHERE profile_id=? AND revoked_at IS NULL").bind(id).first();
  if(count.count<=1)return problem(409,"LAST_DEVICE","마지막 연결 기기는 해제할 수 없습니다.");
  const now=Date.now();
  // The second statement consumes the SQLite changes() value of the first,
  // in one D1 batch transaction. Never keep a leaked link code usable after revoke.
  const [revoke]=await db.batch([
    db.prepare("UPDATE profile_devices SET revoked_at=? WHERE profile_id=? AND device_id=? AND revoked_at IS NULL AND EXISTS(SELECT 1 FROM profile_devices WHERE profile_id=? AND device_id=? AND revoked_at IS NULL) AND (SELECT COUNT(*) FROM profile_devices WHERE profile_id=? AND revoked_at IS NULL)>1").bind(now,id,target,id,owner,id),
    db.prepare("DELETE FROM profile_link_credentials WHERE profile_id=? AND changes()>0").bind(id)
  ]);
  if((revoke.meta?.changes??0)===0)return problem(404,"NOT_FOUND","연결된 기기가 없습니다.");
  return json({data:{revoked_device_id:target,link_code_invalidated:true}});
}

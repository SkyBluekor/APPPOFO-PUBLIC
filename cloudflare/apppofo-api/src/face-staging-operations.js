import { json, problem, noContent, readJson } from "./http.js";
import { remoteFaceEnabled } from "./face-policy.js";
import { requireFaceProfileAccess } from "./face-access.js";
import { actorScope, FaceStoreError, faceStatus } from "./face-repository.js";

export async function authorizeScanner(request, env) {
  if (!remoteFaceEnabled(request, env)) return problem(503,"FACE_NOT_READY","얼굴 검색을 사용할 수 없습니다.");
  if (!["POST","DELETE"].includes(request.method)) return problem(405,"METHOD_NOT_ALLOWED","지원하지 않는 메서드입니다.");
  const received = await readJson(request);
  if (received.error) return received.error;
  const body = received.value;
  if (!body || Object.keys(body).join(",") !== "device_id" ||
      typeof body.device_id !== "string" || !/^[a-f0-9-]{36}$/i.test(body.device_id))
    return problem(400,"INVALID_DEVICE","기기를 확인해 주세요.");
  if (request.method === "DELETE") {
    await env.DB.prepare("UPDATE face_scanner_authorizations SET revoked_at=? WHERE device_id=?")
      .bind(Date.now(),body.device_id).run();
  } else {
    const changed = await env.DB.prepare("INSERT INTO face_scanner_authorizations(device_id,authorized_at) SELECT id,? FROM device_identities WHERE id=? AND revoked_at IS NULL ON CONFLICT(device_id) DO UPDATE SET authorized_at=excluded.authorized_at,revoked_at=NULL")
      .bind(Date.now(),body.device_id).run();
    if (changed.meta?.changes !== 1) return problem(404,"DEVICE_NOT_FOUND","기기를 확인해 주세요.");
  }
  return noContent();
}

export async function chargeFaceMutation(db, actor) {
  const now = Date.now(), cutoff = now - 60000;
  const result = await db.prepare("INSERT INTO face_mutation_rates(actor_scope,window_started_at,request_count) VALUES(?,?,1) ON CONFLICT(actor_scope) DO UPDATE SET request_count=CASE WHEN window_started_at<=? THEN 1 ELSE request_count+1 END,window_started_at=CASE WHEN window_started_at<=? THEN ? ELSE window_started_at END WHERE window_started_at<=? OR request_count<12")
    .bind(actorScope(actor),now,cutoff,cutoff,now,cutoff).run();
  if (result.meta?.changes !== 1) throw new FaceStoreError("FACE_MUTATION_RATE_LIMITED",429);
}

// Turning public recognition OFF requires no new biometric capture or encryption.
// Turning it ON uses the explicit enrollment consent flow. Template revision/AAD stays intact.
export async function withdrawDiscovery(request, env, profileId, actor) {
  const received = await readJson(request);
  if (received.error) return received.error;
  const b = received.value;
  if (!b || Object.keys(b).sort().join(",") !== "discoverable,expected_revision,purge_epoch" ||
      b.discoverable !== false || !Number.isSafeInteger(b.expected_revision) || b.expected_revision<1 ||
      !Number.isSafeInteger(b.purge_epoch) || b.purge_epoch<0)
    throw new FaceStoreError("INVALID_DISCOVERY_WITHDRAWAL",400);
  await requireFaceProfileAccess(env.DB,profileId,actor);
  const id=crypto.randomUUID(), now=Date.now();
  const link=actor.kind==="admin" ? "" : " AND EXISTS(SELECT 1 FROM profile_devices d WHERE d.profile_id=face_consent_state.profile_id AND d.device_id=? AND d.revoked_at IS NULL)";
  await env.DB.batch([
    env.DB.prepare("UPDATE face_consent_state SET discoverable=0,consent_generation=consent_generation+1,updated_at=? WHERE profile_id=? AND revision=? AND storage_consent=1 AND purge_epoch=? AND EXISTS(SELECT 1 FROM face_system_state WHERE singleton=1 AND purge_epoch=?)"+link)
      .bind(now,profileId,b.expected_revision,b.purge_epoch,b.purge_epoch,...(actor.kind==="admin"?[]:[actor.deviceId])),
    env.DB.prepare("INSERT INTO face_operation_guards(request_id,assertion) VALUES(?,changes())").bind(id),
    env.DB.prepare("INSERT INTO face_discovery_audit(event_id,profile_id,actor_scope,revision,at) VALUES(?,?,?,?,?)")
      .bind(id,profileId,actorScope(actor),b.expected_revision,now),
    env.DB.prepare("DELETE FROM face_operation_guards WHERE request_id=?").bind(id),
  ]).catch(()=>{throw new FaceStoreError("FACE_REVISION_CONFLICT");});
  return json({data:await faceStatus(env.DB,profileId,actor,null)});
}

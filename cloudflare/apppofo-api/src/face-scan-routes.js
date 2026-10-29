import { FaceStoreError } from "./face-repository.js";
import { chargeFaceMutation } from "./face-staging-operations.js";
import { scanEnabled, remoteFaceEnabled } from "./face-policy.js";
// Authenticated 1:N matching for local fixtures and explicitly enabled remote environments.
// Never return templates, confidence values, private profile fields or query vectors.
// This module is not enabled by the existing S5-08 enrollment gate alone.
import { json, problem, noContent, readJson } from "./http.js";
import { authenticatedDevice } from "./device-auth.js";
import { FaceInputError, FACE_MODEL_VERSION } from "./face-validation.js";
import { FaceCryptoError, decryptFaceSet, faceKeyFromEnv } from "./face-crypto.js";
import { validateMatchRequest, matchTestSettings, bestPoseScore, chooseMatch } from "./face-match.js";
import { readPublicSummary } from "./profile-summaries.js";

const MAX_CANDIDATES = 25;  // Beyond this, do NOT silently search a partial set.
const REQUESTS_PER_MINUTE = 120;
const MINUTE = 60_000;

function failure(code, status=403) { throw new FaceInputError(code,status); }
function safeBlob(value) {
  if (Array.isArray(value)) return Uint8Array.from(value);
  if (value instanceof Uint8Array) return value;
  if (value instanceof ArrayBuffer) return new Uint8Array(value);
  failure("FACE_MATCH_STORAGE_NOT_READY",503);
}

async function hasActiveLinkedProfile(db, deviceId, remote=false) {
  const row = await db.prepare(
    "SELECT 1 AS ok FROM profile_devices d JOIN profiles p ON p.id=d.profile_id " +
    "WHERE d.device_id=? AND d.revoked_at IS NULL AND p.is_active=1 LIMIT 1")
    .bind(deviceId).first();
  if (row) return true;
  return remote && Boolean(await db.prepare("SELECT 1 AS ok FROM face_scanner_authorizations WHERE device_id=? AND revoked_at IS NULL").bind(deviceId).first());
}
async function checkScanPermission(db,deviceId,policyVersion,remote=false) {
  if (!await hasActiveLinkedProfile(db,deviceId,remote))
    failure("FACE_SCAN_PERMISSION_REQUIRED");
  const grant=await db.prepare(
    "SELECT 1 AS ok FROM face_scan_grants WHERE device_id=? AND " +
    "revoked_at IS NULL AND policy_version=?")
    .bind(deviceId,policyVersion).first();
  if (!grant) failure("FACE_SCAN_PERMISSION_REQUIRED");
}

async function grantScanConsent(request,db,deviceId,policyVersion,remote=false) {
  if (!await hasActiveLinkedProfile(db,deviceId,remote))
    failure("FACE_SCAN_PERMISSION_REQUIRED");
  if (remote) await chargeFaceMutation(db,{kind:"device",deviceId});
  const received=await readJson(request);
  if (received.error) return received.error;
  const body=received.value;
  if (!body || typeof body !== "object" || Array.isArray(body) ||
      Object.keys(body).sort().join(",") !== "consent,policy_version" ||
      body.consent !== true || body.policy_version !== policyVersion)
    failure("FACE_SCAN_CONSENT_REQUIRED");
  const now=Date.now();
  // Re-confirming consent does NOT reset the rate limit.
  await db.prepare("INSERT INTO face_scan_grants(device_id,policy_version,consented_at) " +
    "VALUES(?,?,?) ON CONFLICT(device_id) DO UPDATE SET " +
    "policy_version=excluded.policy_version,consented_at=excluded.consented_at," +
    "revoked_at=NULL").bind(deviceId,policyVersion,now).run();
  return noContent();
}

async function withdrawScanConsent(db,deviceId) {
  await db.prepare("UPDATE face_scan_grants SET revoked_at=? WHERE device_id=? " +
    "AND revoked_at IS NULL").bind(Date.now(),deviceId).run();
  return noContent();
}

async function chargeScanRequest(db,deviceId,policyVersion,remote=false) {
  const now=Date.now(), previous=now-MINUTE;
  const result=await db.prepare(
    "UPDATE face_scan_grants SET " +
    "request_count=CASE WHEN window_started_at<=? THEN 1 ELSE request_count+1 END, " +
    "window_started_at=CASE WHEN window_started_at<=? THEN ? ELSE window_started_at END " +
    "WHERE device_id=? AND revoked_at IS NULL AND policy_version=? " +
    "AND (window_started_at<=? OR request_count<?) " +
    "AND (EXISTS (SELECT 1 FROM profile_devices d JOIN profiles p ON p.id=d.profile_id " +
    "WHERE d.device_id=face_scan_grants.device_id " +
    "AND d.revoked_at IS NULL AND p.is_active=1)" +
    (remote ? " OR EXISTS(SELECT 1 FROM face_scanner_authorizations a WHERE a.device_id=face_scan_grants.device_id AND a.revoked_at IS NULL)" : "") + ")")
    .bind(previous,previous,now,deviceId,policyVersion,previous,REQUESTS_PER_MINUTE).run();
  if (result.meta?.changes !== 1) failure("FACE_SCAN_RATE_LIMITED",429);
}

async function faceCandidates(db,epoch) {
  const query =
    "SELECT p.id,p.display_name,p.headline,t.model_version,t.cipher_suite," +
    "t.key_version,t.nonce,t.ciphertext,t.revision FROM face_templates t " +
    "JOIN profiles p ON p.id=t.profile_id " +
    "JOIN face_consent_state c ON c.profile_id=t.profile_id " +
    "WHERE p.is_active=1 AND c.storage_consent=1 AND c.discoverable=1 " +
    "AND c.revision=t.revision AND c.purge_epoch=? AND t.model_version=? " +
    "ORDER BY p.id LIMIT ?";
  const rows=(await db.prepare(query)
    .bind(epoch,FACE_MODEL_VERSION,MAX_CANDIDATES+1).all()).results;
  if (rows.length>MAX_CANDIDATES)
    failure("FACE_MATCH_CAPACITY_EXCEEDED",503);
  return rows;
}

async function matchFace(request,env,deviceId,policyVersion) {
  const db=env.DB;
  await checkScanPermission(db,deviceId,policyVersion,remoteFaceEnabled(request,env));
  await chargeScanRequest(db,deviceId,policyVersion,remoteFaceEnabled(request,env));
  const received=await readJson(request);
  if (received.error) return received.error;
  const query=validateMatchRequest(received.value);
  try {
  const settings=matchTestSettings(env,remoteFaceEnabled(request,env));
  const epochRow=await db.prepare(
    "SELECT purge_epoch FROM face_system_state WHERE singleton=1").first();
  if (!epochRow || !Number.isSafeInteger(epochRow.purge_epoch))
    failure("FACE_MATCH_STORAGE_NOT_READY",503);
  const epoch=epochRow.purge_epoch;
  const rows=await faceCandidates(db,epoch);
  const scored=[];
  for (const row of rows) {
    const plain=await decryptFaceSet({
      cipherSuite:row.cipher_suite,keyVersion:row.key_version,
      nonce:safeBlob(row.nonce),ciphertext:safeBlob(row.ciphertext),
      modelVersion:row.model_version,revision:row.revision,
    },{profileId:row.id,secretBase64Url:faceKeyFromEnv(env,row.key_version)});
    try {
      scored.push({id:row.id,score:bestPoseScore(query,plain)});
    } finally {
      plain.fill(0); // Clear plaintext even on comparison or validation failures.
    }
  }
  const choice=chooseMatch(scored,settings);

  // Recheck eligibility after ranking, and verify a purge or consent change
  // cannot authorize a stale identity result. In-flight revokes fail closed.
  await checkScanPermission(db,deviceId,policyVersion,remoteFaceEnabled(request,env));
  if (await authenticatedDevice(db,request) !== deviceId)
    return json({matched:false}); // Logout/revocation may occur during async match.
  const currentEpoch=await db.prepare(
    "SELECT purge_epoch FROM face_system_state WHERE singleton=1").first();
  if (!currentEpoch || currentEpoch.purge_epoch!==epoch)
    return json({matched:false});
  if (!choice) return json({matched:false});
  const selected=rows.find(row=>row.id===choice.id);
  const live=await db.prepare(
    "SELECT p.id,p.display_name,p.headline,p.business_card_theme,a.image_base64 AS avatar_base64 FROM profiles p " +
    "LEFT JOIN profile_avatars a ON a.profile_id=p.id " +
    "JOIN face_consent_state c ON c.profile_id=p.id " +
    "JOIN face_templates t ON t.profile_id=p.id " +
    "WHERE p.id=? AND p.is_active=1 AND c.storage_consent=1 " +
    "AND c.discoverable=1 AND c.purge_epoch=? AND c.revision=? " +
    "AND t.revision=? AND t.model_version=?")
    .bind(choice.id,epoch,selected.revision,selected.revision,FACE_MODEL_VERSION).first();
  if (!live) return json({matched:false});
  return json({matched:true,
    profile:{id:live.id,display_name:live.display_name,headline:live.headline,
      business_card_theme:live.business_card_theme,avatar_base64:live.avatar_base64},
    decision_version:settings.decisionVersion});
  } finally { query.fill(0); }
}

export async function handleLocalFaceScan(request,env,deviceId,action,profileId=null) {
  if (!scanEnabled(request,env))
    return problem(503,"FACE_MATCH_NOT_READY","서버 얼굴 검색 기능이 아직 준비되지 않았습니다.");
  const policy=env.APPPOFO_FACE_SCAN_POLICY_VERSION;
  if (typeof policy!=="string" || !policy || policy.length>100)
    return problem(503,"FACE_SCAN_POLICY_NOT_READY","검색 동의 정책이 준비되지 않았습니다.");
  if (action==="consent" && !["GET","POST","DELETE"].includes(request.method))
    return problem(405,"METHOD_NOT_ALLOWED","지원하지 않는 메서드입니다.");
  if (action==="match" && request.method!=="POST")
    return problem(405,"METHOD_NOT_ALLOWED","지원하지 않는 메서드입니다.");
  if (action==="summary" && request.method!=="GET")
    return problem(405,"METHOD_NOT_ALLOWED","지원하지 않는 메서드입니다.");
  try {
    if (action==="summary") {
      const remote=remoteFaceEnabled(request,env);
      await checkScanPermission(env.DB,deviceId,policy,remote);
      if (await authenticatedDevice(env.DB,request)!==deviceId)
        return problem(401,"UNAUTHORIZED","기기 인증이 필요합니다.");
      const summary=await readPublicSummary(env.DB,profileId,FACE_MODEL_VERSION);
      return summary ? json({data:summary}) : problem(404,"NOT_FOUND","공개된 프로필을 확인할 수 없습니다.");
    }
    if (action==="consent" && request.method==="GET") {
      const grant = await env.DB.prepare("SELECT 1 FROM face_scan_grants WHERE device_id=? AND policy_version=? AND revoked_at IS NULL").bind(deviceId,policy).first();
      return json({approved:Boolean(grant) && await hasActiveLinkedProfile(env.DB,deviceId,remoteFaceEnabled(request,env)), policy_version:policy});
    }
    if (action==="consent") return request.method==="POST"
      ? await grantScanConsent(request,env.DB,deviceId,policy,remoteFaceEnabled(request,env))
      : await withdrawScanConsent(env.DB,deviceId);
    return await matchFace(request,env,deviceId,policy);
  } catch(error) {
    if (error instanceof FaceInputError || error instanceof FaceStoreError)
      return problem(error.status,error.code??error.message,
        "얼굴 검색 요청 또는 검색 권한을 확인해 주세요.");
    if (error instanceof FaceCryptoError)
      return problem(503,"FACE_MATCH_UNAVAILABLE",
        "얼굴 검색이 불가능합니다.");
    // Do not log biometrics, authorization, personal data or SQL queries.
    console.error("[FaceMatch] unavailable",error instanceof Error?error.name:"Unknown");
    return problem(503,"FACE_MATCH_UNAVAILABLE","얼굴 검색이 불가능합니다.");
  }
}

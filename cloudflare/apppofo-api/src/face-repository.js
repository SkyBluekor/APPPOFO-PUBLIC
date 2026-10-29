// S5-08-B: All face mutations use one D1.batch transaction with a mandatory
// SQL CHECK assertion after conditional revision CAS. A zero-row UPDATE must
// abort the batch, never partially commit a new face set or idempotency receipt.
import { encryptFaceSet, faceKeyFromEnv, FaceCryptoError } from "./face-crypto.js";
import { validateFaceEnrollment, FaceInputError } from "./face-validation.js";

const DAY = 86_400_000;
const UUID = /^[a-f0-9]{8}-[a-f0-9]{4}-[1-8][a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/i;

export class FaceStoreError extends Error {
  constructor(code, status = 409) {
    super(code); this.name = "FaceStoreError"; this.code = code; this.status = status;
  }
}
const failure = (code, status = 409) => { throw new FaceStoreError(code, status); };
export const actorScope = actor => actor.kind === "admin" ? "admin" : "device:" + actor.deviceId;

function permittedSql(actor) {
  return actor.kind === "admin" ? "" :
    " AND EXISTS(SELECT 1 FROM profile_devices d WHERE d.profile_id=face_consent_state.profile_id AND d.device_id=? AND d.revoked_at IS NULL)";
}
function permittedParams(actor) { return actor.kind === "admin" ? [] : [actor.deviceId]; }
async function sha256Base64Url(data) {
  const bytes = new TextEncoder().encode(data);
  const hashed = new Uint8Array(await crypto.subtle.digest("SHA-256", bytes));
  let binary = "";
  for (const byte of hashed) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}
function key(requestId) {
  if (typeof requestId !== "string" || !UUID.test(requestId))
    failure("INVALID_FACE_REQUEST_ID", 400);
  return requestId.toLowerCase();
}
function casGuard(db, requestId, profileId, newRevision, epoch, scope, operation) {
  return db.prepare(
    "INSERT INTO face_operation_guards(request_id,assertion) VALUES(?, COALESCE(" +
    "(SELECT CASE WHEN revision=? AND last_request_id=? " +
    "AND last_actor_scope=? AND last_operation=? AND purge_epoch=? " +
    "AND (SELECT purge_epoch FROM face_system_state WHERE singleton=1)=? THEN 1 ELSE 0 END " +
    "FROM face_consent_state WHERE profile_id=?),0))")
    .bind(requestId, newRevision, requestId, scope, operation, epoch, epoch, profileId);
}
function isSqlConflict(error) {
  const description = String(error?.message ?? "");
  return /constraint|unique|check|abort|sqlite_constraint/i.test(description);
}
async function globalEpoch(db) {
  const row = await db.prepare(
    "SELECT purge_epoch FROM face_system_state WHERE singleton=1").first();
  if (!row) failure("FACE_SCHEMA_NOT_READY", 503);
  return row.purge_epoch;
}
async function consentRevision(db, id, includeGeneration = false) {
  const state = await db.prepare(
    "SELECT revision,purge_epoch,policy_version,storage_consent,discoverable,updated_at,last_request_id" +
    (includeGeneration ? ",consent_generation " : " ") +
    "FROM face_consent_state WHERE profile_id=?").bind(id).first();
  return state ?? { revision:0,purge_epoch:0,policy_version:null,storage_consent:0,
    discoverable:0,updated_at:null,last_request_id:null };
}
async function receipt(db, actor, operation, requestId) {
  return db.prepare(
    "SELECT target_uuid,payload_digest,result_revision,purge_epoch,expires_at " +
    "FROM face_idempotency WHERE actor_scope=? AND operation=? AND request_id=?")
    .bind(actorScope(actor), operation, requestId).first();
}
export async function faceStatus(db, id, actor, requestId, includeGeneration = false) {
  const epoch = await globalEpoch(db);
  const state = await consentRevision(db, id, includeGeneration);
  const template = await db.prepare(
    "SELECT model_version,revision FROM face_templates WHERE profile_id=?")
    .bind(id).first();
  const registered = Boolean(template && state.storage_consent &&
    state.revision === template.revision && state.purge_epoch === epoch);
  const data = {
    registered, revision: state.revision, purge_epoch: epoch,
    model_version: registered ? template.model_version : null,
    storage_consent: Boolean(state.storage_consent),
    discoverable: registered && Boolean(state.discoverable),
    updated_at: state.updated_at,
  };
  if (includeGeneration) data.consent_generation = state.consent_generation ?? 0;
  if (requestId !== null) {
    const idempotency = await receipt(db, actor, "ENROLL", key(requestId));
    data.request_status = idempotency && idempotency.target_uuid === id &&
      idempotency.purge_epoch === epoch && idempotency.expires_at > Date.now()
      ? "COMMITTED" : "UNKNOWN";
    if (data.request_status === "COMMITTED")
      data.request_revision = idempotency.result_revision;
  }
  return data;
}
function assertCurrentReceipt(old, profileId, digest, epoch) {
  if (old.target_uuid !== profileId || old.payload_digest !== digest)
    failure("IDEMPOTENCY_CONFLICT");
  if (old.purge_epoch !== epoch || old.expires_at <= Date.now())
    failure("REQUEST_EXPIRED");
  return old.result_revision;
}
export async function enrollFace(db, env, profileId, actor, payload, consentGeneration = null) {
  const now = Date.now();
  const parsed = validateFaceEnrollment(payload, {
    approvedPolicyVersion: env.APPPOFO_FACE_POLICY_VERSION, now,
    isAdministrator: actor.kind === "admin",
  });
  const epoch = await globalEpoch(db);
  const digest = await sha256Base64Url(JSON.stringify(payload));
  const previous = await receipt(db, actor, "ENROLL", parsed.requestId);
  if (previous) {
    const revision = assertCurrentReceipt(previous, profileId, digest, epoch);
    const state = await consentRevision(db, profileId);
    const template = await db.prepare("SELECT revision FROM face_templates WHERE profile_id=?")
      .bind(profileId).first();
    // A historical receipt must never claim enrollment is currently active after
    // a newer replacement/deletion. Direct clients to the current GET state.
    if (state.revision !== revision || !state.storage_consent ||
        template?.revision !== revision)
      failure("FACE_STATE_CHANGED");
    return { registered:true, revision, model_version:parsed.modelVersion,
      request_id:parsed.requestId, replayed:true };
  }
  if (parsed.purgeEpoch !== epoch) failure("FACE_PURGE_CONFLICT");
  const state = await consentRevision(db, profileId);
  if (state.revision !== parsed.expectedRevision) failure("FACE_REVISION_CONFLICT");
  const nextRevision = parsed.expectedRevision + 1;
  if (!Number.isSafeInteger(nextRevision)) failure("FACE_REVISION_CONFLICT");
  const encoded = await encryptFaceSet(parsed.embeddings, {
    profileId, modelVersion: parsed.modelVersion, revision:nextRevision,
    keyVersion:1, secretBase64Url:faceKeyFromEnv(env, 1),
  });
  const scope = actorScope(actor);
  // SQLite/D1 binds numeric byte arrays as BLOB. The JS node:sqlite shim in
  // local tests translates these arrays back to Uint8Array.
  const commands = [
    db.prepare("INSERT INTO face_consent_state(profile_id,revision,purge_epoch) " +
      "SELECT ?,0,? WHERE EXISTS(SELECT 1 FROM profiles WHERE id=?) " +
      "ON CONFLICT(profile_id) DO NOTHING").bind(profileId,epoch,profileId),
    db.prepare("INSERT INTO face_idempotency(actor_scope,operation,request_id,target_uuid," +
      "payload_digest,result_revision,purge_epoch,expires_at,created_at) " +
      "VALUES(?,'ENROLL',?,?,?,?,?,?,?)").bind(scope,parsed.requestId,profileId,digest,
      nextRevision,epoch,now + DAY,now),
    db.prepare("UPDATE face_consent_state SET revision=revision+1, " +
      "policy_version=?,storage_consent=1,discoverable=?,confirmed_at=?," +
      "withdrawn_at=NULL,updated_at=?,last_request_id=?," +
      "last_actor_scope=?,last_operation='ENROLL' " +
      "WHERE profile_id=? AND revision=? AND purge_epoch=? " +
      "AND EXISTS(SELECT 1 FROM face_system_state WHERE singleton=1 AND purge_epoch=?) " +
      "AND EXISTS(SELECT 1 FROM profiles WHERE id=face_consent_state.profile_id AND is_active=1)" +
      permittedSql(actor) + (consentGeneration === null ? "" : " AND consent_generation=?")).bind(parsed.consent.policyVersion,
      parsed.consent.discoverable?1:0,now,now,parsed.requestId,scope,profileId,
      parsed.expectedRevision,epoch,epoch,...permittedParams(actor),...(consentGeneration === null ? [] : [consentGeneration])),
    casGuard(db,parsed.requestId,profileId,nextRevision,epoch,scope,"ENROLL"),
    db.prepare("INSERT INTO face_templates(profile_id,model_version,cipher_suite,key_version," +
      "nonce,ciphertext,revision,enrolled_at,updated_at) VALUES(?,?,?,?,?,?,?,?,?) " +
      "ON CONFLICT(profile_id) DO UPDATE SET " +
      "model_version=excluded.model_version,cipher_suite=excluded.cipher_suite," +
      "key_version=excluded.key_version,nonce=excluded.nonce," +
      "ciphertext=excluded.ciphertext,revision=excluded.revision," +
      "enrolled_at=excluded.enrolled_at,updated_at=excluded.updated_at")
      .bind(profileId,parsed.modelVersion,encoded.cipherSuite,1,
        Array.from(encoded.nonce),Array.from(encoded.ciphertext),nextRevision,now,now),
    db.prepare("INSERT INTO face_consent_audit(event_id,profile_id,action,actor_scope," +
      "policy_version,revision,at) VALUES(?,?,'ENROLL',?,?,?,?)")
      .bind(crypto.randomUUID(),profileId,scope,parsed.consent.policyVersion,nextRevision,now),
    db.prepare("DELETE FROM face_operation_guards WHERE request_id=?").bind(parsed.requestId),
  ];
  try { await db.batch(commands); }
  catch (error) {
    if (!isSqlConflict(error)) throw error;
    const raced = await receipt(db, actor, "ENROLL", parsed.requestId);
    if (raced) {
      const revision = assertCurrentReceipt(raced,profileId,digest,epoch);
      const current = await consentRevision(db,profileId);
      if (current.revision !== revision || !current.storage_consent)
        failure("FACE_STATE_CHANGED");
      return { registered:true, revision, model_version:parsed.modelVersion,
        request_id:parsed.requestId, replayed:true };
    }
    failure("FACE_REVISION_CONFLICT");
  }
  return { registered:true,revision:nextRevision,model_version:parsed.modelVersion,
    request_id:parsed.requestId,replayed:false };
}
export function validateFaceDelete(payload) {
  if (!payload || typeof payload !== "object" || Array.isArray(payload) ||
      Object.keys(payload).sort().join(",") !==
        "expected_revision,purge_epoch,request_id")
    failure("INVALID_FACE_DELETE_BODY",400);
  const requestId = key(payload.request_id);
  if (![payload.expected_revision,payload.purge_epoch].every(
    n => Number.isSafeInteger(n) && n >= 0))
    failure("INVALID_FACE_REVISION",400);
  return { requestId, expectedRevision:payload.expected_revision,
    purgeEpoch:payload.purge_epoch };
}
export async function deleteFace(db, profileId, actor, body) {
  const parsed = validateFaceDelete(body);
  const epoch = await globalEpoch(db);
  const digest = await sha256Base64Url(JSON.stringify(body));
  const previous = await receipt(db,actor,"DELETE",parsed.requestId);
  if (previous) {
    const revision = assertCurrentReceipt(previous,profileId,digest,epoch);
    const current = await consentRevision(db,profileId);
    if (current.revision !== revision || current.storage_consent)
      failure("FACE_STATE_CHANGED");
    return;
  }
  if (parsed.purgeEpoch !== epoch) failure("FACE_PURGE_CONFLICT");
  const state = await consentRevision(db,profileId);
  if (state.revision !== parsed.expectedRevision) failure("FACE_REVISION_CONFLICT");
  const nextRevision = parsed.expectedRevision+1;
  const now = Date.now(), scope = actorScope(actor);
  const commands = [
    db.prepare("INSERT INTO face_consent_state(profile_id,revision,purge_epoch) " +
      "SELECT ?,0,? WHERE EXISTS(SELECT 1 FROM profiles WHERE id=?) " +
      "ON CONFLICT(profile_id) DO NOTHING").bind(profileId,epoch,profileId),
    db.prepare("INSERT INTO face_idempotency(actor_scope,operation,request_id,target_uuid," +
      "payload_digest,result_revision,purge_epoch,expires_at,created_at) " +
      "VALUES(?,'DELETE',?,?,?,?,?,?,?)")
      .bind(scope,parsed.requestId,profileId,digest,nextRevision,epoch,now+DAY,now),
    db.prepare("UPDATE face_consent_state SET revision=revision+1," +
      "storage_consent=0,discoverable=0,withdrawn_at=?,updated_at=?," +
      "last_request_id=?,last_actor_scope=?,last_operation='DELETE' " +
      "WHERE profile_id=? AND revision=? AND purge_epoch=? " +
      "AND EXISTS(SELECT 1 FROM face_system_state WHERE singleton=1 AND purge_epoch=?)" +
      permittedSql(actor)).bind(now,now,parsed.requestId,scope,profileId,
      parsed.expectedRevision,epoch,epoch,...permittedParams(actor)),
    casGuard(db,parsed.requestId,profileId,nextRevision,epoch,scope,"DELETE"),
    db.prepare("DELETE FROM face_templates WHERE profile_id=?").bind(profileId),
    db.prepare("INSERT INTO face_consent_audit(event_id,profile_id,action,actor_scope," +
      "policy_version,revision,at) VALUES(?,?,'DELETE',?,?,?,?)")
      .bind(crypto.randomUUID(),profileId,scope,state.policy_version,nextRevision,now),
    db.prepare("DELETE FROM face_operation_guards WHERE request_id=?").bind(parsed.requestId),
  ];
  try { await db.batch(commands); }
  catch (error) {
    if (!isSqlConflict(error)) throw error;
    const raced = await receipt(db,actor,"DELETE",parsed.requestId);
    if (raced) { assertCurrentReceipt(raced,profileId,digest,epoch);return; }
    failure("FACE_REVISION_CONFLICT");
  }
}

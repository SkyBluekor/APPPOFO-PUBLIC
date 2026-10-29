// S5-08-B: administrator-only, two-phase destructive *face-only* purge.
// Called from authenticated admin router. Never deletes profiles/profile_items.
import { FaceStoreError } from "./face-repository.js";

const TTL_MS = 3 * 60_000;
const fail = (code,status=409) => { throw new FaceStoreError(code,status); };
const uuid = value => typeof value === "string" &&
  /^[a-f0-9]{8}-[a-f0-9]{4}-[1-8][a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/i.test(value);
async function sha256Base64Url(text) {
  const bytes = new Uint8Array(await crypto.subtle.digest(
    "SHA-256",new TextEncoder().encode(text)));
  let binary="";
  for(const byte of bytes)binary+=String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g,"-").replace(/\//g,"_").replace(/=+$/,"");
}
function strictConfirm(body) {
  if(!body || typeof body!=="object" || Array.isArray(body) ||
     Object.keys(body).sort().join(",") !==
       "confirmation_token,expected_count,operation_id,purge_epoch")
    fail("INVALID_FACE_PURGE_CONFIRM",400);
  if(!uuid(body.operation_id) || !uuid(body.confirmation_token) ||
     !Number.isSafeInteger(body.expected_count) || body.expected_count<0 ||
     !Number.isSafeInteger(body.purge_epoch) || body.purge_epoch<0)
    fail("INVALID_FACE_PURGE_CONFIRM",400);
  return body;
}

export async function prepareFacePurge(db) {
  const now=Date.now(), expires=now+TTL_MS;
  const system=await db.prepare(
    "SELECT purge_epoch FROM face_system_state WHERE singleton=1").first();
  if(!system)fail("FACE_SCHEMA_NOT_READY",503);
  const count=(await db.prepare("SELECT count(*) AS n FROM face_templates").first()).n;
  const operationId=crypto.randomUUID();
  const confirmationToken=crypto.randomUUID();
  await db.prepare("INSERT INTO face_admin_operations(operation_id,token_hash,expires_at," +
    "purge_epoch,expected_count,created_at) VALUES(?,?,?,?,?,?)")
    .bind(operationId,await sha256Base64Url(confirmationToken),
      expires,system.purge_epoch,count,now).run();
  return {operation_id:operationId,confirmation_token:confirmationToken,
    expected_count:count,purge_epoch:system.purge_epoch,expires_at:expires};
}

export async function confirmFacePurge(db,body) {
  const {operation_id:operationId,confirmation_token:token,
    expected_count:expectedCount,purge_epoch:epoch}=strictConfirm(body);
  const now=Date.now(), digest=await sha256Base64Url(token);
  const pending=await db.prepare(
    "SELECT operation_id FROM face_admin_operations WHERE operation_id=? " +
    "AND token_hash=? AND expires_at>? AND used_at IS NULL AND purge_epoch=? " +
    "AND expected_count=?")
    .bind(operationId,digest,now,epoch,expectedCount).first();
  if(!pending)fail("FACE_PURGE_CONFIRM_INVALID");
  const current=await db.prepare("SELECT purge_epoch FROM face_system_state WHERE singleton=1")
    .first();
  if(!current || current.purge_epoch!==epoch)fail("FACE_PURGE_CONFLICT");
  const count=(await db.prepare("SELECT count(*) AS n FROM face_templates").first()).n;
  if(count!==expectedCount)fail("FACE_PURGE_COUNT_CHANGED");

  // Atomic CAS: require current operation unused, unexpired, same epoch and
  // candidate count. The CHECK guard aborts DB.batch if either UPDATE touched 0.
  const guards=[
    db.prepare("UPDATE face_admin_operations SET used_at=? WHERE operation_id=? " +
      "AND token_hash=? AND used_at IS NULL AND expires_at>? AND purge_epoch=? " +
      "AND expected_count=(SELECT count(*) FROM face_templates)")
      .bind(now,operationId,digest,now,epoch),
    db.prepare("UPDATE face_system_state SET purge_epoch=purge_epoch+1 " +
      "WHERE singleton=1 AND purge_epoch=? AND EXISTS(" +
      "SELECT 1 FROM face_admin_operations WHERE operation_id=? AND used_at=?)")
      .bind(epoch,operationId,now),
    db.prepare("INSERT INTO face_operation_guards(request_id,assertion) " +
      "VALUES(?,COALESCE((SELECT CASE WHEN a.used_at=? AND a.purge_epoch=? " +
      "AND (SELECT purge_epoch FROM face_system_state WHERE singleton=1)=? " +
      "THEN 1 ELSE 0 END FROM face_admin_operations a WHERE a.operation_id=?),0))")
      .bind(operationId,now,epoch,epoch+1,operationId),
    db.prepare("DELETE FROM face_templates"),
    db.prepare("UPDATE face_consent_state SET revision=revision+1,purge_epoch=?," +
      "storage_consent=0,discoverable=0,withdrawn_at=?,updated_at=?," +
      "last_request_id=NULL,last_actor_scope=NULL,last_operation=NULL").bind(epoch+1,now,now),
    db.prepare("INSERT INTO face_consent_audit(event_id,profile_id,action," +
      "actor_scope,policy_version,revision,at) " +
      "VALUES(?,NULL,'PURGE','admin',NULL,NULL,?)").bind(crypto.randomUUID(),now),
    db.prepare("UPDATE face_admin_operations SET deleted_count=? WHERE operation_id=?")
      .bind(expectedCount,operationId),
    db.prepare("DELETE FROM face_operation_guards WHERE request_id=?").bind(operationId),
  ];
  try{await db.batch(guards);}
  catch(error){
    if(/constraint|unique|check|abort/i.test(String(error?.message??"")))
      fail("FACE_PURGE_CONFLICT");
    throw error;
  }
  return { operation_id:operationId, deleted_count:expectedCount,purge_epoch:epoch+1 };
}

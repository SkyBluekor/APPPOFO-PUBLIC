import { createProfile } from "./repository.js";
import { createMine } from "./mobile-repository.js";
import { FaceStoreError } from "./face-repository.js";

export async function createProfileOnce(db, owner, profile, requestId) {
  // Existing clients without a key keep their original contract.
  if (requestId === null) return owner === null ? createProfile(db, profile) : createMine(db, owner, profile);
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(requestId))
    throw new FaceStoreError("INVALID_IDEMPOTENCY_KEY", 400);
  requestId = requestId.toLowerCase();
  const scope = owner === null ? "admin" : "device:" + owner;
  const digest = Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256",
    new TextEncoder().encode(JSON.stringify(profile))))).map(x=>x.toString(16).padStart(2,"0")).join("");
  async function previous() {
    const row = await db.prepare("SELECT payload_digest,profile_id,created_at FROM profile_create_receipts WHERE actor_scope=? AND request_id=?")
      .bind(scope, requestId).first();
    if (!row) return null;
    if (row.payload_digest !== digest) throw new FaceStoreError("IDEMPOTENCY_CONFLICT");
    const live = owner === null
      ? await db.prepare("SELECT id FROM profiles WHERE id=?").bind(row.profile_id).first()
      : await db.prepare("SELECT profile_id AS id FROM profile_devices WHERE profile_id=? AND device_id=? AND revoked_at IS NULL")
          .bind(row.profile_id,owner).first();
    if (!live) throw new FaceStoreError("CREATED_PROFILE_NO_LONGER_ACCESSIBLE");
    return { id: row.profile_id, created_at: row.created_at, updated_at: row.created_at };
  }
  const old = await previous();
  if (old) return old;
  const id = crypto.randomUUID(), now = Date.now();
  const receipt = db.prepare("INSERT INTO profile_create_receipts(actor_scope,request_id,payload_digest,profile_id,created_at) VALUES(?,?,?,?,?)")
    .bind(scope, requestId, digest, id, now);
  try {
    return owner === null ? await createProfile(db, profile, {id, now, receipt}) :
      await createMine(db, owner, profile, {id, now, receipt});
  } catch (error) {
    const raced = await previous();
    if (raced) return raced;
    throw error;
  }
}

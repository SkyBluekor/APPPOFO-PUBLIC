// S5-08-B: face-specific authorization policy.
// Call only AFTER the router has independently verified the bearer session.
// A profile UUID is not a permission to access biometric records.
import { validCloudId } from "./validation.js";

export class FaceAccessError extends Error {
  constructor(code, status) {
    super(code);
    this.name = "FaceAccessError";
    this.code = code;
    this.status = status;
  }
}

export async function requireFaceProfileAccess(db, profileId, actor,
  { requireActive = false } = {}) {
  if (!validCloudId(profileId)) throw new FaceAccessError("INVALID_ID", 400);
  if (!db || typeof db.prepare !== "function" ||
      !actor || !["admin", "device"].includes(actor.kind))
    throw new FaceAccessError("FACE_ACCESS_NOT_CONFIGURED", 503);

  let profile;
  if (actor.kind === "admin") {
    // 'admin' is supplied only by the authenticated admin router; never from JSON.
    profile = await db.prepare(
      "SELECT id,is_active FROM profiles WHERE id=?").bind(profileId).first();
  } else {
    if (typeof actor.deviceId !== "string" || !actor.deviceId)
      throw new FaceAccessError("UNAUTHORIZED", 401);
    // Check live link state at the time of the request. No cached client roles.
    profile = await db.prepare(
      "SELECT p.id,p.is_active FROM profiles p " +
      "JOIN profile_devices d ON d.profile_id=p.id " +
      "WHERE p.id=? AND d.device_id=? AND d.revoked_at IS NULL")
      .bind(profileId, actor.deviceId).first();
  }
  // Hide whether a different person's UUID exists.
  if (!profile) throw new FaceAccessError("NOT_FOUND", 404);
  if (requireActive && profile.is_active !== 1)
    throw new FaceAccessError("INACTIVE_PROFILE", 409);
  return { profileId: profile.id, active: profile.is_active === 1 };
}

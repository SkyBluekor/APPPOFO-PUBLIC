// S5-08-B: strict, transport-only face enrollment validation.
// Keep biometric bytes out of profile CRUD and all diagnostic logs.
export const FACE_MODEL_VERSION = "sface_2021dec";
export const FACE_POSES = 3;
export const FACE_DIMENSION = 128;
export const FACE_BINARY_BYTES = FACE_POSES * FACE_DIMENSION * 4;
export const FACE_POLICY_MAX_BYTES = 4096;
const NORM_TOLERANCE = 0.001;
const ALLOWED = new Set([
  "request_id", "expected_revision", "purge_epoch", "model_version",
  "embeddings_b64url", "consent",
]);
const CONSENT_ALLOWED = new Set([
  "policy_version", "storage_opt_in", "discoverable_opt_in",
  "confirmed_at", "subject_present",
]);
const UUID = /^[a-f0-9]{8}-[a-f0-9]{4}-[1-8][a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/i;
const BASE64URL = /^[A-Za-z0-9_-]+$/;
const isObject = v => v !== null && typeof v === "object" && !Array.isArray(v);

export class FaceInputError extends Error {
  constructor(code, status = 400) {
    super(code);
    this.name = "FaceInputError";
    this.code = code;
    this.status = status;
  }
}

function fail(code, status = 400) { throw new FaceInputError(code, status); }
function exactKeys(value, allowed) {
  if (!isObject(value)) fail("INVALID_FACE_PAYLOAD");
  for (const key of Object.keys(value))
    if (!allowed.has(key)) fail("UNKNOWN_FACE_FIELD");
  for (const key of allowed)
    if (!Object.hasOwn(value, key)) fail("MISSING_FACE_FIELD");
}
function revision(value) {
  if (!Number.isSafeInteger(value) || value < 0) fail("INVALID_FACE_REVISION");
  return value;
}

// RFC 4648 canonical unpadded base64url. Reject aliases, padding and whitespace.
export function decodeFaceBase64Url(text, byteLength = FACE_BINARY_BYTES) {
  if (typeof text !== "string" || text.length !== Math.ceil(byteLength * 8 / 6) ||
      !BASE64URL.test(text)) fail("INVALID_FACE_ENCODING");
  let raw;
  try { raw = atob(text.replace(/-/g, "+").replace(/_/g, "/")); }
  catch { fail("INVALID_FACE_ENCODING"); }
  if (raw.length !== byteLength) fail("INVALID_FACE_ENCODING");
  const encoded = btoa(raw).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  if (encoded !== text) fail("INVALID_FACE_ENCODING");
  return Uint8Array.from(raw, ch => ch.charCodeAt(0));
}

export function encodeFaceBase64Url(bytes) {
  if (!(bytes instanceof Uint8Array)) throw new TypeError("Expected Uint8Array");
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

// Check all 3 independent poses. A valid overall norm cannot hide an invalid pose.
export function validateFaceVectors(bytes) {
  if (!(bytes instanceof Uint8Array) || bytes.byteLength !== FACE_BINARY_BYTES)
    fail("INVALID_FACE_VECTOR_LENGTH");
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  for (let pose = 0; pose < FACE_POSES; pose++) {
    let sum = 0;
    for (let dim = 0; dim < FACE_DIMENSION; dim++) {
      const value = view.getFloat32((pose * FACE_DIMENSION + dim) * 4, true);
      if (!Number.isFinite(value) || Math.abs(value) > 1)
        fail("INVALID_FACE_VECTOR_VALUE");
      sum += value * value;
    }
    if (Math.abs(Math.sqrt(sum) - 1) > NORM_TOLERANCE)
      fail("INVALID_FACE_VECTOR_NORM");
  }
  return bytes;
}

// The approved policy version must come from the server configuration, NOT the client.
// This function only validates consent claims; it never proves physical presence.
export function validateFaceEnrollment(payload, { approvedPolicyVersion, now = Date.now(),
  isAdministrator = false } = {}) {
  if (typeof approvedPolicyVersion !== "string" || !approvedPolicyVersion)
    fail("FACE_POLICY_NOT_CONFIGURED", 503);
  exactKeys(payload, ALLOWED);
  const encodedLength = new TextEncoder().encode(JSON.stringify(payload)).length;
  if (encodedLength > FACE_POLICY_MAX_BYTES) fail("FACE_PAYLOAD_TOO_LARGE", 413);
  if (typeof payload.request_id !== "string" || !UUID.test(payload.request_id))
    fail("INVALID_FACE_REQUEST_ID");
  if (payload.model_version !== FACE_MODEL_VERSION)
    fail("UNSUPPORTED_FACE_MODEL", 422);
  exactKeys(payload.consent, CONSENT_ALLOWED);
  const consent = payload.consent;
  if (consent.policy_version !== approvedPolicyVersion)
    fail("FACE_POLICY_VERSION_MISMATCH", 422);
  if (consent.storage_opt_in !== true ||
      typeof consent.discoverable_opt_in !== "boolean" ||
      typeof consent.subject_present !== "boolean")
    fail("FACE_CONSENT_REQUIRED", 403);
  if (isAdministrator && !consent.subject_present)
    fail("FACE_SUBJECT_PRESENCE_REQUIRED", 403);
  if (!Number.isSafeInteger(consent.confirmed_at) ||
      consent.confirmed_at < now - 15 * 60_000 ||
      consent.confirmed_at > now + 60_000)
    fail("FACE_CONSENT_EXPIRED", 403);
  const bytes = validateFaceVectors(decodeFaceBase64Url(payload.embeddings_b64url));
  return {
    requestId: payload.request_id.toLowerCase(),
    expectedRevision: revision(payload.expected_revision),
    purgeEpoch: revision(payload.purge_epoch),
    modelVersion: payload.model_version,
    embeddings: bytes,
    consent: {
      policyVersion: consent.policy_version,
      storageAllowed: true,
      discoverable: consent.discoverable_opt_in,
      confirmedAt: now, // server clock is authoritative for auditing
      subjectPresentClaim: consent.subject_present,
    },
  };
}

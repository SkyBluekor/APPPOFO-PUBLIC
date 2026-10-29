// S5-08-B: AES-256-GCM for a single, already-validated 3-pose face set.
// No D1 or transport dependency. The key must be a dedicated Worker Secret,
// never the administrator token. This is server-side encryption, not E2EE.
import {
  FACE_BINARY_BYTES, FACE_MODEL_VERSION, decodeFaceBase64Url,
} from "./face-validation.js";

const AAD_SCHEMA_VERSION = 1;
const SUITE = "AES-256-GCM";
const NONCE_BYTES = 12;
const TAG_BITS = 128;
const encoder = new TextEncoder();
const UUID = /^[a-f0-9]{8}-[a-f0-9]{4}-[1-8][a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/i;

export class FaceCryptoError extends Error {
  constructor(code) {
    super(code);
    this.name = "FaceCryptoError";
    this.code = code;
  }
}

function fail(code) { throw new FaceCryptoError(code); }
function params(profileId, modelVersion, revision, keyVersion) {
  if (typeof profileId !== "string" || !UUID.test(profileId) ||
      modelVersion !== FACE_MODEL_VERSION ||
      !Number.isSafeInteger(revision) || revision <= 0 ||
      !Number.isSafeInteger(keyVersion) || keyVersion < 1 || keyVersion > 999)
    fail("INVALID_FACE_AAD");
  // Fixed array ordering, no optional JSON object properties or ambiguity.
  return encoder.encode(JSON.stringify([
    AAD_SCHEMA_VERSION, profileId.toLowerCase(), modelVersion, revision, keyVersion,
  ]));
}

async function secretKey(secretBase64Url, usage) {
  if (typeof secretBase64Url !== "string" || secretBase64Url.length !== 43)
    fail("FACE_KEY_UNAVAILABLE");
  let bytes;
  try { bytes = decodeFaceBase64Url(secretBase64Url, 32); }
  catch { fail("FACE_KEY_UNAVAILABLE"); }
  try {
    return await crypto.subtle.importKey("raw", bytes, { name: "AES-GCM" }, false, [usage]);
  } catch { fail("FACE_KEY_UNAVAILABLE"); }
  finally { bytes.fill(0); }
}

export async function encryptFaceSet(plain, { profileId, modelVersion, revision,
  keyVersion, secretBase64Url }) {
  if (!(plain instanceof Uint8Array) || plain.byteLength !== FACE_BINARY_BYTES)
    fail("INVALID_FACE_PLAINTEXT");
  const additionalData = params(profileId, modelVersion, revision, keyVersion);
  const key = await secretKey(secretBase64Url, "encrypt");
  const nonce = crypto.getRandomValues(new Uint8Array(NONCE_BYTES));
  // Web Crypto returns ciphertext || authentication tag; store as one BLOB.
  try {
    const result = await crypto.subtle.encrypt(
      { name: "AES-GCM", iv: nonce, additionalData, tagLength: TAG_BITS }, key, plain);
    return {
      cipherSuite: SUITE, keyVersion, nonce,
      ciphertext: new Uint8Array(result),
      modelVersion, revision,
    };
  } catch { fail("FACE_ENCRYPT_FAILED"); }
}

export async function decryptFaceSet(record, { profileId, secretBase64Url }) {
  if (!record || record.cipherSuite !== SUITE ||
      !(record.nonce instanceof Uint8Array) ||
      record.nonce.byteLength !== NONCE_BYTES ||
      !(record.ciphertext instanceof Uint8Array) ||
      record.ciphertext.byteLength !== FACE_BINARY_BYTES + TAG_BITS / 8)
    fail("INVALID_FACE_CIPHERTEXT");
  const additionalData = params(
    profileId, record.modelVersion, record.revision, record.keyVersion);
  const key = await secretKey(secretBase64Url, "decrypt");
  try {
    const result = await crypto.subtle.decrypt(
      { name: "AES-GCM", iv: record.nonce, additionalData, tagLength: TAG_BITS },
      key, record.ciphertext);
    return new Uint8Array(result);
  } catch {
    // Never surface raw vectors, key material, tag or underlying CryptoError.
    fail("FACE_DECRYPT_FAILED");
  }
}

// Explicitly select only allowlisted versioned secrets on the server.
// No fallback to APPPOFO_API_TOKEN or a caller-controlled key name.
export function faceKeyFromEnv(env, keyVersion) {
  if (!Number.isSafeInteger(keyVersion) || keyVersion < 1 || keyVersion > 999)
    fail("FACE_KEY_UNAVAILABLE");
  if (keyVersion === 1 && typeof env?.APPPOFO_FACE_KEY_V1 === "string")
    return env.APPPOFO_FACE_KEY_V1;
  fail("FACE_KEY_UNAVAILABLE");
}

import test from "node:test";
import assert from "node:assert/strict";
import { randomBytes } from "node:crypto";
import { DatabaseSync } from "node:sqlite";
import { readFileSync } from "node:fs";
import {
  FACE_BINARY_BYTES, FACE_MODEL_VERSION, FaceInputError,
  decodeFaceBase64Url, encodeFaceBase64Url, validateFaceEnrollment,
} from "../src/face-validation.js";
import { encryptFaceSet, decryptFaceSet, faceKeyFromEnv, FaceCryptoError } from "../src/face-crypto.js";
import { requireFaceProfileAccess, FaceAccessError } from "../src/face-access.js";

const profileId = "11111111-1111-4111-8111-111111111111";
const deviceId = "a-test-device";
const policy = "apppofo-test-policy-v1"; // synthetic, NOT an approved real-world consent notice
const secret = randomBytes(32).toString("base64url");

function sampleVectors() {
  const bytes = new Uint8Array(FACE_BINARY_BYTES);
  const view = new DataView(bytes.buffer);
  for (let pose = 0; pose < 3; pose++)
    view.setFloat32((pose * 128 + pose) * 4, 1, true);
  return bytes;
}
function body(modifications = {}) {
  const base = {
    request_id: "22222222-2222-4222-8222-222222222222",
    expected_revision: 0,
    purge_epoch: 0,
    model_version: FACE_MODEL_VERSION,
    embeddings_b64url: encodeFaceBase64Url(sampleVectors()),
    consent: {
      policy_version: policy,
      storage_opt_in: true,
      discoverable_opt_in: false,
      confirmed_at: Date.now(),
      subject_present: false,
    },
  };
  return { ...base, ...modifications };
}
function rejectsFace(fn, code) {
  assert.throws(fn, error => error instanceof FaceInputError && error.code === code);
}

test("S5-08-B face payload accepts exactly 3 independent 128D normalized vectors", () => {
  const input = body();
  const parsed = validateFaceEnrollment(input, { approvedPolicyVersion: policy });
  assert.equal(parsed.embeddings.byteLength, 1536);
  assert.deepEqual(parsed.embeddings, sampleVectors());
  assert.equal(parsed.modelVersion, "sface_2021dec");
  assert.equal(parsed.consent.storageAllowed, true);
  assert.equal(parsed.consent.discoverable, false,
    "Private enrollment is valid even when public discovery is declined.");
  assert.equal(parsed.expectedRevision, 0);
});

test("face payload fails closed without a server-approved consent policy", () => {
  rejectsFace(() => validateFaceEnrollment(body()), "FACE_POLICY_NOT_CONFIGURED");
  rejectsFace(() => validateFaceEnrollment(body({consent:{
    ...body().consent, policy_version: "attacker-policy",
  }}), { approvedPolicyVersion: policy }), "FACE_POLICY_VERSION_MISMATCH");
  rejectsFace(() => validateFaceEnrollment(body({consent:{
    ...body().consent, storage_opt_in: false,
  }}), { approvedPolicyVersion: policy }), "FACE_CONSENT_REQUIRED");
  rejectsFace(() => validateFaceEnrollment(body({consent:{
    ...body().consent, subject_present: false,
  }}), { approvedPolicyVersion: policy, isAdministrator: true }),
  "FACE_SUBJECT_PRESENCE_REQUIRED");
  rejectsFace(() => validateFaceEnrollment(body({consent:{
    ...body().consent, confirmed_at: Date.now() - 16 * 60_000,
  }}), { approvedPolicyVersion: policy }), "FACE_CONSENT_EXPIRED");
});

test("face binary rejects noncanonical encoding, dimensions and unsafe values", () => {
  const invalidEncoding = body({ embeddings_b64url: "AA==" });
  rejectsFace(() => validateFaceEnrollment(invalidEncoding,
    { approvedPolicyVersion: policy }), "INVALID_FACE_ENCODING");
  const invalidLength = body({embeddings_b64url:
    encodeFaceBase64Url(sampleVectors().subarray(0, 1532))});
  rejectsFace(() => validateFaceEnrollment(invalidLength,
    { approvedPolicyVersion: policy }), "INVALID_FACE_ENCODING");
  for (const [value, expected] of [
    [NaN, "INVALID_FACE_VECTOR_VALUE"],
    [Infinity, "INVALID_FACE_VECTOR_VALUE"],
    [0.5, "INVALID_FACE_VECTOR_NORM"],
    [2, "INVALID_FACE_VECTOR_VALUE"],
  ]) {
    const bytes = sampleVectors();
    new DataView(bytes.buffer).setFloat32(0, value, true);
    rejectsFace(() => validateFaceEnrollment(body({
      embeddings_b64url: encodeFaceBase64Url(bytes),
    }), { approvedPolicyVersion: policy }), expected);
  }
});

test("rejects injected fields, wrong model/revision and malformed ids", () => {
  for (const [mutation, code] of [
    [{ avatar_path: "private.png" }, "UNKNOWN_FACE_FIELD"],
    [{ face_embeddings: [] }, "UNKNOWN_FACE_FIELD"],
    [{ model_version: "sface_custom" }, "UNSUPPORTED_FACE_MODEL"],
    [{ expected_revision: -1 }, "INVALID_FACE_REVISION"],
    [{ request_id: "invalid" }, "INVALID_FACE_REQUEST_ID"],
  ]) {
    rejectsFace(() => validateFaceEnrollment(body(mutation),
      { approvedPolicyVersion: policy }), code);
  }
  rejectsFace(() => validateFaceEnrollment(body({
    consent: { ...body().consent, admin_approved: true },
  }), { approvedPolicyVersion: policy }), "UNKNOWN_FACE_FIELD");
});

test("AES-GCM roundtrip, unique nonce, immutable AAD, no key fallback", async () => {
  const plain = sampleVectors();
  const params = {
    profileId, modelVersion: FACE_MODEL_VERSION, revision: 1,
    keyVersion: 1, secretBase64Url: secret,
  };
  const first = await encryptFaceSet(plain, params);
  const second = await encryptFaceSet(plain, params);
  assert.equal(first.cipherSuite, "AES-256-GCM");
  assert.equal(first.nonce.byteLength, 12);
  assert.equal(first.ciphertext.byteLength, 1552);
  assert.notDeepEqual(first.nonce, second.nonce);
  assert.notDeepEqual(first.ciphertext, plain);
  assert.deepEqual(await decryptFaceSet(first, params), plain);
  assert.equal(faceKeyFromEnv({ APPPOFO_FACE_KEY_V1: secret }, 1), secret);
  assert.throws(() => faceKeyFromEnv({ APPPOFO_API_TOKEN: secret }, 1),
    error => error instanceof FaceCryptoError && error.code === "FACE_KEY_UNAVAILABLE");
});

test("AES-GCM rejects tampered ciphertext, modified AAD and incorrect keys", async () => {
  const params = {
    profileId, modelVersion: FACE_MODEL_VERSION, revision: 2,
    keyVersion: 1, secretBase64Url: secret,
  };
  const encrypted = await encryptFaceSet(sampleVectors(), params);
  async function fails(record, options, code = "FACE_DECRYPT_FAILED") {
    await assert.rejects(decryptFaceSet(record, options),
      error => error instanceof FaceCryptoError && error.code === code);
  }
  await fails(encrypted, { ...params, profileId: "33333333-3333-4333-8333-333333333333" });
  await fails({ ...encrypted, revision: 3 }, params);
  await fails({ ...encrypted, keyVersion: 2 }, params);
  await fails(encrypted, { ...params, secretBase64Url: randomBytes(32).toString("base64url") });
  const corrupted = { ...encrypted, ciphertext: Uint8Array.from(encrypted.ciphertext) };
  corrupted.ciphertext[0] ^= 1;
  await fails(corrupted, params);
  await assert.rejects(encryptFaceSet(sampleVectors(), {
    ...params, secretBase64Url: "",
  }), error => error instanceof FaceCryptoError && error.code === "FACE_KEY_UNAVAILABLE");
});

test("live profile-device link is required; revoked link cannot access faces", async () => {
  const schema = ["0001_profiles.sql", "0002_device_ownership.sql", "0003_profile_connections.sql"]
    .map(name => readFileSync(new URL("../migrations/" + name, import.meta.url), "utf8")).join("\n");
  const db = new DatabaseSync(":memory:");
  try {
    db.exec("PRAGMA foreign_keys=ON;");
    db.exec(schema);
    db.prepare("INSERT INTO profiles(id,display_name,created_at,updated_at) VALUES(?,?,?,?)")
      .run(profileId, "가상 사용자", 1, 1);
    db.prepare("INSERT INTO device_identities(id,public_key_jwk,key_thumbprint,created_at) VALUES(?,?,?,?)")
      .run(deviceId, "{}", "test-thumbprint", 1);
    db.prepare("INSERT INTO profile_devices(profile_id,device_id,granted_at) VALUES(?,?,?)")
      .run(profileId, deviceId, 1);
    const d1 = {
      prepare: sql => ({ bind: (...args) => ({first: async () => db.prepare(sql).get(...args) ?? null}) }),
    };
    const get = (actor) => requireFaceProfileAccess(d1, profileId, actor);
    assert.equal((await get({kind:"device",deviceId})).profileId, profileId);
    assert.equal((await get({kind:"admin"})).profileId, profileId);
    await assert.rejects(get({kind:"device",deviceId:"intruder"}),
      error => error instanceof FaceAccessError && error.status === 404);
    db.prepare("UPDATE profile_devices SET revoked_at=? WHERE profile_id=? AND device_id=?")
      .run(10, profileId, deviceId);
    await assert.rejects(get({kind:"device",deviceId}),
      error => error instanceof FaceAccessError && error.status === 404);
    db.prepare("UPDATE profiles SET is_active=0 WHERE id=?").run(profileId);
    await assert.rejects(requireFaceProfileAccess(d1, profileId, {kind:"admin"},
      {requireActive:true}), error => error instanceof FaceAccessError && error.status === 409);
  } finally { db.close(); }
});

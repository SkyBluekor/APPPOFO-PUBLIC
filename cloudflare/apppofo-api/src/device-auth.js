import { json, problem, readJson } from "./http.js";

const encoder = new TextEncoder();
const CHALLENGE_TTL = 2 * 60 * 1000;
const SESSION_TTL = 30 * 60 * 1000;
const b64Pattern = /^[A-Za-z0-9_-]+$/;

function encodeUrl(bytes) {
  let value = "";
  for (const byte of bytes) value += String.fromCharCode(byte);
  return btoa(value).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}
function decodeUrl(text, min, max = min) {
  if (typeof text !== "string" || !b64Pattern.test(text) ||
      text.length > Math.ceil(max * 4 / 3) + 2) return null;
  try {
    const value = text.replace(/-/g, "+").replace(/_/g, "/");
    const raw = atob(value + "=".repeat((4 - value.length % 4) % 4));
    if (raw.length < min || raw.length > max) return null;
    const bytes = Uint8Array.from(raw, (char) => char.charCodeAt(0));
    return encodeUrl(bytes) === text ? bytes : null;
  } catch { return null; }
}
async function digestUrl(value) {
  return encodeUrl(new Uint8Array(await crypto.subtle.digest("SHA-256", encoder.encode(value))));
}
function onlyKeys(obj, allowed) {
  return obj && typeof obj === "object" && !Array.isArray(obj) &&
    Object.keys(obj).every((key) => allowed.includes(key));
}
function validId(value) {
  return typeof value === "string" &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}
function identityKey(jwk) {
  return onlyKeys(jwk, ["kty", "crv", "x", "y"]) &&
    jwk.kty === "EC" && jwk.crv === "P-256" &&
    decodeUrl(jwk.x, 32) && decodeUrl(jwk.y, 32);
}
function thumbprint(jwk) {
  // Versioned canonical representation, independent of JSON field order.
  return digestUrl("AppPofo-P256-v1\n" + jwk.x + "\n" + jwk.y);
}
async function importPublicKey(jwk) {
  if (!identityKey(jwk)) return null;
  try {
    return await crypto.subtle.importKey("jwk", { ...jwk, ext: true },
      { name: "ECDSA", namedCurve: "P-256" }, false, ["verify"]);
  } catch { return null; }
}

// AndroidKeyStore's SHA256withECDSA returns strict ASN.1 DER. WebCrypto requires
// IEEE P1363 (r || s); parse strict short-form DER with positive integers only.
function derToRaw(bytes) {
  if (!bytes || bytes.length < 8 || bytes.length > 72 || bytes[0] !== 0x30 ||
      bytes[1] !== bytes.length - 2) return null;
  let offset = 2;
  const values = [];
  for (let i = 0; i < 2; i++) {
    if (bytes[offset++] !== 0x02) return null;
    let len = bytes[offset++];
    if (len < 1 || len > 33 || offset + len > bytes.length) return null;
    let slice = bytes.slice(offset, offset + len);
    if (slice[0] & 0x80) return null;
    if (slice.length === 33) {
      if (slice[0] !== 0 || !(slice[1] & 0x80)) return null;
      slice = slice.slice(1);
    } else if (slice.length > 1 && slice[0] === 0 && !(slice[1] & 0x80)) return null;
    if (slice.length > 32) return null;
    const padded = new Uint8Array(32);
    padded.set(slice, 32 - slice.length);
    values.push(padded);
    offset += len;
  }
  if (offset !== bytes.length) return null;
  const raw = new Uint8Array(64);
  raw.set(values[0], 0);
  raw.set(values[1], 32);
  return raw;
}
async function verifyProof(jwk, message, signature) {
  const key = await importPublicKey(jwk);
  const raw = derToRaw(decodeUrl(signature, 8, 72));
  if (!key || !raw) return false;
  try {
    return await crypto.subtle.verify({ name: "ECDSA", hash: "SHA-256" },
      key, raw, encoder.encode(message));
  } catch { return false; }
}
export function proofMessage(purpose, challenge, subject) {
  return "AppPofo-v1\n" + purpose + "\n" + challenge + "\n" + subject;
}
function failure() {
  // Avoid leaking device existence or the public key registered to an ID.
  return problem(401, "DEVICE_AUTH_FAILED", "기기 인증에 실패했습니다.");
}
async function challenge(request, db) {
  const parsed = await readJson(request);
  if (parsed.error) return parsed.error;
  const body = parsed.value;
  if (!onlyKeys(body, ["purpose", "public_key", "device_id"])) {
    return problem(400, "INVALID_INPUT", "요청 형식이 올바르지 않습니다.");
  }
  let subject;
  if (body.purpose === "register" && !("device_id" in body)) {
    if (!(await importPublicKey(body.public_key))) return failure();
    subject = await thumbprint(body.public_key);
    // Bound per-key short term challenge creation. Edge/WAF rate limiting is
    // additionally recommended against attacks rotating keys.
  } else if (body.purpose === "session" && !("public_key" in body) && validId(body.device_id)) {
    subject = body.device_id;
    const device = await db.prepare(
      "SELECT id FROM device_identities WHERE id = ? AND revoked_at IS NULL"
    ).bind(subject).first();
    if (!device) return failure();
  } else return failure();

  const now = Date.now();
  const count = await db.prepare(
    "SELECT COUNT(*) n FROM device_challenges WHERE purpose = ? AND subject = ? AND expires_at > ?"
  ).bind(body.purpose, subject, now).first();
  if (count.n >= 3) return problem(429, "RATE_LIMITED", "잠시 뒤 다시 시도하세요.");
  const id = crypto.randomUUID();
  await db.prepare(
    "INSERT INTO device_challenges(id,purpose,subject,expires_at) VALUES(?,?,?,?)"
  ).bind(id, body.purpose, subject, now + CHALLENGE_TTL).run();
  return json({ challenge_id: id, expires_at: now + CHALLENGE_TTL }, 201);
}
async function verifyChallenge(db, purpose, challengeId, subject, jwk, signature) {
  if (!validId(challengeId) || typeof signature !== "string") return false;
  const now = Date.now();
  const row = await db.prepare(
    "SELECT id FROM device_challenges WHERE id=? AND purpose=? AND subject=? AND expires_at>?"
  ).bind(challengeId, purpose, subject, now).first();
  if (!row || !(await verifyProof(jwk, proofMessage(purpose,challengeId,subject),signature)))
    return false;
  // Atomic single-use consumption; competing requests cannot both pass.
  const used = await db.prepare(
    "DELETE FROM device_challenges WHERE id=? AND purpose=? AND subject=? AND expires_at>?"
  ).bind(challengeId, purpose, subject, Date.now()).run();
  return (used.meta?.changes ?? 0) === 1;
}
async function register(request, db) {
  const parsed = await readJson(request);
  if (parsed.error) return parsed.error;
  const body = parsed.value;
  if (!onlyKeys(body, ["challenge_id","public_key","signature"])) return failure();
  const jwk = body.public_key;
  if (!(await importPublicKey(jwk))) return failure();
  const keyId = await thumbprint(jwk);
  if (!(await verifyChallenge(db,"register",body.challenge_id,keyId,jwk,body.signature)))
    return failure();
  const existing = await db.prepare(
    "SELECT id,revoked_at FROM device_identities WHERE key_thumbprint=?"
  ).bind(keyId).first();
  // A fresh proof of the same private key can recover a lost registration reply.
  // Consumed challenges still cannot be replayed, and revoked identities stay revoked.
  if (existing) return existing.revoked_at === null
    ? json({device_id:existing.id},201) : failure();
  const id = crypto.randomUUID();
  await db.prepare(
    "INSERT INTO device_identities(id,public_key_jwk,key_thumbprint,created_at) VALUES(?,?,?,?)"
  ).bind(id, JSON.stringify(jwk), keyId, Date.now()).run();
  return json({ device_id: id },201);
}
async function startSession(request, db) {
  const parsed = await readJson(request);
  if (parsed.error) return parsed.error;
  const body = parsed.value;
  if (!onlyKeys(body, ["challenge_id","device_id","signature"]) || !validId(body.device_id))
    return failure();
  const device = await db.prepare(
    "SELECT public_key_jwk FROM device_identities WHERE id=? AND revoked_at IS NULL"
  ).bind(body.device_id).first();
  if (!device) return failure();
  const jwk = JSON.parse(device.public_key_jwk);
  if (!(await verifyChallenge(db,"session",body.challenge_id,body.device_id,jwk,body.signature)))
    return failure();
  const raw = new Uint8Array(32);
  crypto.getRandomValues(raw);
  const token = "appdev_" + encodeUrl(raw);
  const expires = Date.now() + SESSION_TTL;
  await db.prepare(
    "INSERT INTO device_sessions(token_hash,device_id,expires_at) VALUES(?,?,?)"
  ).bind(await digestUrl(token),body.device_id,expires).run();
  return json({ access_token:token, token_type:"Bearer", expires_at:expires });
}
export async function authenticatedDevice(db, request) {
  const token = /^Bearer (appdev_[A-Za-z0-9_-]{43})$/.exec(
    request.headers.get("authorization") ?? "")?.[1];
  if (!token) return null;
  const row = await db.prepare(
    `SELECT ds.device_id FROM device_sessions ds
     JOIN device_identities d ON d.id=ds.device_id
     WHERE ds.token_hash=? AND ds.expires_at>? AND ds.revoked_at IS NULL
       AND d.revoked_at IS NULL`
  ).bind(await digestUrl(token),Date.now()).first();
  return row?.device_id ?? null;
}
export async function handleDeviceRoute(request,db,path) {
  if (request.method !== "POST") return problem(405,"METHOD_NOT_ALLOWED","지원하지 않는 메서드입니다.");
  if (path === "/api/device/challenge") return challenge(request,db);
  if (path === "/api/device/register") return register(request,db);
  if (path === "/api/device/session") return startSession(request,db);
  return problem(404,"NOT_FOUND","주소를 찾을 수 없습니다.");
}

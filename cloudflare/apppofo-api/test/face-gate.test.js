import test from "node:test";
import assert from "node:assert/strict";
import { createHash, randomBytes } from "node:crypto";
import { DatabaseSync } from "node:sqlite";
import { readFileSync } from "node:fs";
import worker from "../src/index.js";

const id = "11111111-1111-4111-8111-111111111111";
const deviceId = "face-test-device";
const adminToken = "test-only-face-gate-admin-secret-1234567890";

function fixture() {
  const db = new DatabaseSync(":memory:");
  const schema = ["0001_profiles.sql", "0002_device_ownership.sql", "0003_profile_connections.sql"]
    .map(name => readFileSync(new URL("../migrations/" + name, import.meta.url), "utf8")).join("\n");
  db.exec("PRAGMA foreign_keys=ON;");
  db.exec(schema);
  db.prepare("INSERT INTO profiles(id,display_name,created_at,updated_at) VALUES(?,?,?,?)")
    .run(id, "가상 인물", 1, 1);
  db.prepare("INSERT INTO device_identities(id,public_key_jwk,key_thumbprint,created_at) VALUES(?,?,?,?)")
    .run(deviceId, "{}", "face-gate-thumbprint", 1);
  db.prepare("INSERT INTO profile_devices(profile_id,device_id,granted_at) VALUES(?,?,?)")
    .run(id, deviceId, 1);
  const deviceToken = "appdev_" + randomBytes(32).toString("base64url");
  db.prepare("INSERT INTO device_sessions(token_hash,device_id,expires_at) VALUES(?,?,?)")
    .run(createHash("sha256").update(deviceToken).digest("base64url"),
      deviceId, Date.now() + 60_000);

  const DB = {
    prepare: sql => ({
      bind: (...args) => ({
        first: async () => db.prepare(sql).get(...args) ?? null,
      }),
    }),
  };
  const env = { DB, APPPOFO_API_TOKEN: adminToken };
  const send = (path, method = "GET", token = adminToken) => worker.fetch(
    new Request("https://appofo.test" + path, {
      method,
      headers: token ? { Authorization: "Bearer " + token } : {},
    }), env);
  return { db, send, deviceToken };
}

test("face GET/POST/DELETE require administrator authentication and never store pre-migration", async () => {
  const f = fixture();
  try {
    for (const method of ["GET", "POST", "DELETE"]) {
      assert.equal((await f.send("/api/profiles/" + id + "/face", method, "")).status, 401);
      const response = await f.send("/api/profiles/" + id + "/face", method);
      assert.equal(response.status, 503);
      assert.equal((await response.json()).error.code, "FACE_ENROLLMENT_NOT_READY");
    }
    assert.equal((await f.send("/api/profiles/" + id + "/face", "PUT")).status, 405);
    assert.equal((await f.send("/api/profiles/not-a-uuid/face")).status, 400);
    assert.equal((await f.send(
      "/api/profiles/33333333-3333-4333-8333-333333333333/face")).status, 404);
    assert.equal(dbHasFaceTable(f.db), false);
  } finally { f.db.close(); }
});

test("face user routes accept only live linked device, never admin bearer", async () => {
  const f = fixture();
  try {
    const route = "/api/me/profiles/" + id + "/face";
    assert.equal((await f.send(route)).status, 401,
      "Administrator bearer must never be treated as a device bearer.");
    assert.equal((await f.send(route, "GET", "")).status, 401);
    for (const method of ["GET", "POST", "DELETE"]) {
      assert.equal((await f.send(route, method, f.deviceToken)).status, 503);
    }
    f.db.prepare("UPDATE profile_devices SET revoked_at=? WHERE profile_id=? AND device_id=?")
      .run(10, id, deviceId);
    assert.equal((await f.send(route, "GET", f.deviceToken)).status, 404);
    f.db.prepare("UPDATE device_sessions SET revoked_at=? WHERE device_id=?")
      .run(11, deviceId);
    assert.equal((await f.send(route, "GET", f.deviceToken)).status, 401);
    assert.equal(dbHasFaceTable(f.db), false);
  } finally { f.db.close(); }
});

function dbHasFaceTable(db) {
  return db.prepare("SELECT 1 FROM sqlite_master WHERE name='face_templates'").get() !== undefined;
}

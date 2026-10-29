import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { remoteFaceEnabled, PRODUCTION_FACE_HOST } from "../src/face-policy.js";

test("public example cannot activate remote faces or inherit production identifiers", () => {
  const config = JSON.parse(readFileSync(new URL("../wrangler.jsonc", import.meta.url), "utf8"));
  assert.equal(config.workers_dev, false);
  assert.equal(config.vars.APPPOFO_DEPLOYMENT_STAGE, "public-example");
  assert.equal(config.vars.APPPOFO_FACE_REMOTE_ENABLED, "disabled");
  assert.equal(config.d1_databases[0].database_id, "00000000-0000-4000-8000-000000000001");
  assert.ok(PRODUCTION_FACE_HOST.endsWith(".invalid"));
  assert.equal(remoteFaceEnabled(new Request("https://" + PRODUCTION_FACE_HOST + "/api/me/face/config"), config.vars), false);
  for (const name of ["APPPOFO_API_TOKEN", "APPPOFO_FACE_KEY_V1"])
    assert.equal(config.vars[name], undefined);
});

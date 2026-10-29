import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync, writeFileSync, mkdirSync, mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { parseLocalVars } from "../tools/face_local_vars.mjs";
import { buildFaceLocalConfig, createFaceLocalConfig, isLocalFaceMatchEnabled, FACE_LOCAL_KEYS, FACE_MATCH_LOCAL_KEYS } from "../tools/build_face_local_config.mjs";

test("face local parser agrees with Wrangler for quoted and unquoted dummy dev vars", () => {
  const sample = [
    "\uFEFFAPPPOFO_API_TOKEN=\"synthetic-private-example-not-a-real-admin-token\"",
    "APPPOFO_FACE_KEY_V1='synthetic-not-a-real-face-key'",
    "APPPOFO_FACE_LOCAL_TEST_MODE=enabled",
    "APPPOFO_FACE_POLICY_VERSION=local-face-test-v1 # comment",
    "# ignored comment",
  ].join("\r\n");
  const vars = parseLocalVars(sample);
  assert.equal(vars.APPPOFO_API_TOKEN, "synthetic-private-example-not-a-real-admin-token");
  assert.equal(vars.APPPOFO_FACE_KEY_V1, "synthetic-not-a-real-face-key");
  assert.equal(vars.APPPOFO_FACE_LOCAL_TEST_MODE, "enabled");
  assert.equal(vars.APPPOFO_FACE_POLICY_VERSION, "local-face-test-v1");
  assert.equal(Object.keys(vars).length, 4);
});

test("face local parser fails closed on duplicate or mismatched quotes", () => {
  assert.throws(() => parseLocalVars("A=first\nA=second"), /Duplicate local variable name/);
  assert.throws(() => parseLocalVars('A="unclosed'), /Unterminated quoted local variable/);
});

test("local-only Wrangler config declares all face test bindings without modifying production", () => {
  const raw = readFileSync(new URL("../wrangler.jsonc", import.meta.url), "utf8");
  const original = JSON.parse(raw);
  const snapshot = JSON.stringify(original);
  const derived = buildFaceLocalConfig(original);
  assert.deepEqual(
    derived.secrets.required.filter(x => FACE_LOCAL_KEYS.includes(x)).sort(),
    [...FACE_LOCAL_KEYS].sort()
  );
  assert.equal(derived.main, original.main);
  assert.deepEqual(derived.d1_databases, original.d1_databases);
  assert.equal(JSON.stringify(original), snapshot, "Must not mutate checked-in configuration");
  assert.deepEqual(original.secrets.required, ["APPPOFO_API_TOKEN","APPPOFO_FACE_KEY_V1"],
    "Production requires secret names, without embedding secret values");
  assert.equal(derived.vars,undefined,"Local development must not inherit production rollout flags");
  assert.equal(derived.APPPOFO_FACE_KEY_V1, undefined);
  assert.equal(derived.vars?.APPPOFO_FACE_KEY_V1, undefined);
});

test("local matching test bindings remain opt-in with a production rollout config", () => {
  const original=JSON.parse(readFileSync(new URL("../wrangler.jsonc",import.meta.url),"utf8"));
  const without=buildFaceLocalConfig(original);
  assert.equal(FACE_MATCH_LOCAL_KEYS.some(k=>without.secrets.required.includes(k)),false);
  const withScan=buildFaceLocalConfig(original,true);
  assert.equal(FACE_MATCH_LOCAL_KEYS.every(k=>withScan.secrets.required.includes(k)),true);
  assert.equal(FACE_MATCH_LOCAL_KEYS.some(k=>original.secrets.required.includes(k)),false);
  assert.equal(withScan.vars,undefined,"Do not put matching secrets in checked-in config");
});

test("face match opt-in detection reads unquoted and quoted local settings", () => {
  assert.equal(isLocalFaceMatchEnabled([
    "APPPOFO_FACE_LOCAL_TEST_MODE=enabled",
    "APPPOFO_FACE_MATCH_LOCAL_TEST_MODE=enabled",
  ].join("\r\n")), true);
  assert.equal(isLocalFaceMatchEnabled([
    "APPPOFO_FACE_LOCAL_TEST_MODE='enabled'",
    'APPPOFO_FACE_MATCH_LOCAL_TEST_MODE="enabled"',
  ].join("\n")), true);
  assert.equal(isLocalFaceMatchEnabled([
    "APPPOFO_FACE_LOCAL_TEST_MODE=enabled",
    "APPPOFO_FACE_MATCH_LOCAL_TEST_MODE=disabled",
  ].join("\n")), false);
  assert.equal(isLocalFaceMatchEnabled(
    "APPPOFO_FACE_MATCH_LOCAL_TEST_MODE=enabled"), false);
});

test("generated localhost Wrangler config actually includes the scanner opt-in", () => {
  const root = mkdtempSync(join(tmpdir(), "apppofo-face-local-config-"));
  try {
    mkdirSync(join(root, "tools"));
    const production = JSON.parse(readFileSync(new URL("../wrangler.jsonc", import.meta.url), "utf8"));
    writeFileSync(join(root, "wrangler.jsonc"), JSON.stringify(production));
    const varsPath = join(root, ".dev.vars");
    const rootUrl = pathToFileURL(join(root, "tools", "build_face_local_config.mjs")).href;
    const baseVars = "APPPOFO_FACE_LOCAL_TEST_MODE=enabled\n";
    writeFileSync(varsPath, baseVars + "APPPOFO_FACE_MATCH_LOCAL_TEST_MODE=enabled\n");
    let path = createFaceLocalConfig(rootUrl);
    let generated = JSON.parse(readFileSync(path, "utf8"));
    assert.equal(FACE_MATCH_LOCAL_KEYS.every(k => generated.secrets.required.includes(k)), true,
      "Explicit local scan opt-in must reach generated Wrangler secrets.required");
    assert.equal(generated.secrets.required.includes("APPPOFO_API_TOKEN"), true);
    assert.equal(generated.vars, undefined, "Never write secret values in generated config");

    writeFileSync(varsPath, baseVars);
    path = createFaceLocalConfig(rootUrl);
    generated = JSON.parse(readFileSync(path, "utf8"));
    assert.equal(FACE_MATCH_LOCAL_KEYS.some(k => generated.secrets.required.includes(k)), false,
      "Local scanner defaults off without a separate opt-in");
    assert.deepEqual(production.secrets.required, ["APPPOFO_API_TOKEN","APPPOFO_FACE_KEY_V1"],
      "Creating a local override must not mutate production configuration");
  } finally {
    rmSync(root, {recursive:true, force:true});
  }
});

test("local Wrangler config creation rejects invalid production configuration", () => {
  assert.throws(() => buildFaceLocalConfig({}), /Unexpected Wrangler/);
  assert.throws(() => buildFaceLocalConfig({secrets:{required:["APPPOFO_API_TOKEN"]},
    main:"src/index.js"}), /DB binding/);
});

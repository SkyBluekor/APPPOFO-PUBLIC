// Creates a local-only Wrangler config with additional dev secrets.
// Never modifies wrangler.jsonc, stores secret values, or deploys a Worker.
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { resolve } from "node:path";
import { parseLocalVars } from "./face_local_vars.mjs";

export const FACE_LOCAL_KEYS = Object.freeze([
  "APPPOFO_API_TOKEN",
  "APPPOFO_FACE_KEY_V1",
  "APPPOFO_FACE_LOCAL_TEST_MODE",
  "APPPOFO_FACE_POLICY_VERSION",
]);

export const FACE_MATCH_LOCAL_KEYS = Object.freeze([
  "APPPOFO_FACE_MATCH_LOCAL_TEST_MODE",
  "APPPOFO_FACE_SCAN_POLICY_VERSION",
  "APPPOFO_FACE_MATCH_THRESHOLD_TEST",
  "APPPOFO_FACE_MATCH_MARGIN_TEST",
]);

export function buildFaceLocalConfig(original, enableLocalMatch = false) {
  if (!original || typeof original !== "object" || Array.isArray(original) ||
      !Array.isArray(original.secrets?.required) ||
      !original.secrets.required.includes("APPPOFO_API_TOKEN")) {
    throw new Error("Unexpected Wrangler secrets configuration; refusing local override.");
  }
  if (!original.main || !Array.isArray(original.d1_databases) ||
      !original.d1_databases.some((item) => item.binding === "DB"))
    throw new Error("Local Wrangler configuration has no main entry or DB binding.");
  const result = structuredClone(original);
  // Local .dev.vars supplies its own policies. Never inherit production rollout flags.
  delete result.vars;
  result.secrets.required = [...new Set([
    ...result.secrets.required, ...FACE_LOCAL_KEYS,
    ...(enableLocalMatch ? FACE_MATCH_LOCAL_KEYS : []),
  ])];
  return result;
}

// Parse exactly the same values that Wrangler reads from .dev.vars.
// A mistaken literal-backslash regex here silently disabled LOCAL scanner
// bindings even when the PowerShell setup had opted in.
export function isLocalFaceMatchEnabled(devVarsText) {
  const vars = parseLocalVars(devVarsText);
  return vars.APPPOFO_FACE_LOCAL_TEST_MODE === "enabled" &&
    vars.APPPOFO_FACE_MATCH_LOCAL_TEST_MODE === "enabled";
}

export function createFaceLocalConfig(rootUrl) {
  const sourceUrl = new URL("../wrangler.jsonc", rootUrl);
  const outputUrl = new URL("../.wrangler.face-local.jsonc", rootUrl);
  // Checked-in wrangler.jsonc is plain JSON. Fail closed if this changes.
  const original = JSON.parse(readFileSync(sourceUrl, "utf8"));
  const localVarsText = readFileSync(new URL("../.dev.vars", rootUrl), "utf8");
  const testMode = isLocalFaceMatchEnabled(localVarsText);
  const local = buildFaceLocalConfig(original, testMode);
  writeFileSync(outputUrl, JSON.stringify(local, null, 2) + "\n", {
    encoding: "utf8", mode: 0o600,
  });
  return outputUrl;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  createFaceLocalConfig(import.meta.url);
  console.log("Created local-only Wrangler config (secret names only; no values).");
}

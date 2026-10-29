// Read-only verification of an explicitly selected LOCAL face enrollment.
// Uses exactly 127.0.0.1 and prints no secrets or embedding bytes.
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { parseLocalVars } from "./face_local_vars.mjs";

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
export async function localFaceStatus(uuid, vars, fetchFn = fetch) {
  if (typeof uuid !== "string" || !UUID.test(uuid))
    throw new Error("Valid LOCAL profile UUID required.");
  if (vars?.APPPOFO_FACE_LOCAL_TEST_MODE !== "enabled" ||
      typeof vars.APPPOFO_API_TOKEN !== "string" ||
      vars.APPPOFO_API_TOKEN.length < 32)
    throw new Error("Local test authorization not configured.");
  const url = "http://127.0.0.1:8787/api/profiles/" + uuid + "/face";
  const response = await fetchFn(url, {
    method: "GET", redirect: "error",
    headers: {Authorization: "Bearer " + vars.APPPOFO_API_TOKEN},
    signal: AbortSignal.timeout(8000),
  });
  if (response.status !== 200)
    throw new Error("Local face status unavailable (HTTP " + response.status + ").");
  const data = (await response.json())?.data;
  if (!data || typeof data.registered !== "boolean" ||
      !Number.isSafeInteger(data.revision) || data.revision < 0 ||
      !Number.isSafeInteger(data.purge_epoch) || data.purge_epoch < 0)
    throw new Error("Malformed face status response.");
  return {
    uuid,
    registered: data.registered,
    revision: data.revision,
    purge_epoch: data.purge_epoch,
    model_version: data.registered ? data.model_version : null,
    discoverable: data.discoverable === true,
  };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const vars = parseLocalVars(
      readFileSync(new URL("../.dev.vars", import.meta.url), "utf8"));
    const status = await localFaceStatus(process.argv[2], vars);
    console.log("LOCAL UUID: " + status.uuid);
    console.log("Face enrolled: " + (status.registered ? "YES" : "NO"));
    console.log("Revision: " + status.revision);
    console.log("Model: " + (status.model_version ?? "(not registered)"));
    console.log("Discoverable: " + (status.discoverable ? "YES" : "NO"));
    console.log("Read-only verification: no profile or face data changed.");
  } catch (error) {
    console.error("Local face status check failed: " + error.message);
    process.exitCode = 1;
  }
}

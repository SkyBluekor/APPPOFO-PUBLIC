// Explicitly delete a captured LOCAL face template after testing.
// No production endpoints, automatic cleanup, or secrets in output.
import { readFileSync } from "node:fs";
import { randomUUID } from "node:crypto";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { parseLocalVars } from "./face_local_vars.mjs";

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const LOOPBACK = "http://127.0.0.1:8787";

export async function deleteLocalFace(uuid, localVars, fetchFn = fetch) {
  if (typeof uuid !== "string" || !UUID.test(uuid))
    throw new Error("A canonical local UUID is required.");
  if (localVars?.APPPOFO_FACE_LOCAL_TEST_MODE !== "enabled" ||
      typeof localVars.APPPOFO_API_TOKEN !== "string" ||
      localVars.APPPOFO_API_TOKEN.length < 32)
    throw new Error("Local face test authorization is not configured.");

  const endpoint = LOOPBACK + "/api/profiles/" + uuid + "/face";
  const call = async (method, body) => {
    const response = await fetchFn(endpoint, {
      method,
      redirect: "error",
      headers: {
        Authorization: "Bearer " + localVars.APPPOFO_API_TOKEN,
        ...(body ? {"Content-Type": "application/json"} : {}),
      },
      ...(body ? {body: JSON.stringify(body)} : {}),
      signal: AbortSignal.timeout(8000),
    });
    const data = response.status === 204 ? null : await response.json();
    return {status: response.status, data};
  };

  const before = await call("GET");
  if (before.status !== 200 || !before.data?.data)
    throw new Error("Cannot verify LOCAL face status (HTTP " + before.status + ").");
  const state = before.data.data;
  if (state.registered === false) return "ALREADY_EMPTY";
  if (state.registered !== true || !Number.isSafeInteger(state.revision) ||
      state.revision < 0 || !Number.isSafeInteger(state.purge_epoch) ||
      state.purge_epoch < 0)
    throw new Error("Unexpected local face status; refusing deletion.");

  const removed = await call("DELETE", {
    request_id: randomUUID(),
    expected_revision: state.revision,
    purge_epoch: state.purge_epoch,
  });
  if (removed.status !== 204)
    throw new Error("Local face DELETE was rejected (HTTP " + removed.status + ").");

  const after = await call("GET");
  if (after.status !== 200 || after.data?.data?.registered !== false ||
      after.data.data.revision !== state.revision + 1)
    throw new Error("DELETE response received, but final local state was not verified.");
  return "DELETED";
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const [uuid, confirmation] = process.argv.slice(2);
  if (confirmation !== "--confirm-delete-local-face") {
    console.error("Usage: node tools/face_local_cleanup.mjs <LOCAL-UUID> --confirm-delete-local-face");
    process.exitCode = 2;
  } else {
    try {
      const env = parseLocalVars(
        readFileSync(new URL("../.dev.vars", import.meta.url), "utf8"));
      const result = await deleteLocalFace(uuid, env);
      console.log(result === "DELETED" ?
        "PASS: local encrypted face template deleted and final state verified." :
        "PASS: local face was already unregistered; no delete sent.");
      console.log("Local text profile and registration audit remain.");
    } catch (error) {
      console.error("Local cleanup not confirmed: " + error.message);
      process.exitCode = 1;
    }
  }
}

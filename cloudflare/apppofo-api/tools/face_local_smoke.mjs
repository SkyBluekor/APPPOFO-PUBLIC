// S5-08-B local-only HTTP smoke test using synthetic 3x128D features.
// Never sends real camera images, real embeddings, production tokens or network
// requests outside 127.0.0.1. Refuses existing/modified face state.
import { readFileSync } from "node:fs";
import { randomUUID } from "node:crypto";
import { parseLocalVars } from "./face_local_vars.mjs";

const uuid = process.argv[2];
if (!uuid || !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(uuid)) {
  console.error("Usage: node tools/face_local_smoke.mjs <LOCAL-PROFILE-UUID>");
  process.exit(2);
}
const env = parseLocalVars(readFileSync(new URL("../.dev.vars", import.meta.url), "utf8"));
if (env.APPPOFO_FACE_LOCAL_TEST_MODE !== "enabled" ||
  typeof env.APPPOFO_API_TOKEN !== "string" || env.APPPOFO_API_TOKEN.length < 32 ||
  !env.APPPOFO_FACE_POLICY_VERSION) {
  throw new Error("Missing local-only settings. Run start_face_local.ps1 first.");
}
const url = "http://127.0.0.1:8787/api/profiles/" + uuid + "/face";
async function call(method, body, suffix = "") {
  const response = await fetch(url + suffix, {
    method,
    redirect:"error",
    headers:{
      Authorization: "Bearer " + env.APPPOFO_API_TOKEN,
      ...(body ? { "Content-Type": "application/json" } : {}),
    },
    ...(body ? {body:JSON.stringify(body)} : {}),
    signal:AbortSignal.timeout(8000),
  });
  const data = response.status === 204 ? null : await response.json();
  return { status:response.status, data };
}
function assert(ok, message) { if (!ok) throw new Error(message); }
const initial = await call("GET");
if (initial.status === 401) {
  throw new Error("Local Worker rejected the development token (401). " +
    "Restart Wrangler with the latest local-only config; never print the token.");
}
assert(initial.status === 200, "Local face status unavailable (HTTP " + initial.status + ")");
const state = initial.data?.data;
assert(state && !state.registered && state.revision === 0 && state.purge_epoch === 0,
  "Refusing smoke test: this face profile has prior local state. Use a fresh local-only UUID.");
const bytes=new Uint8Array(1536),dv=new DataView(bytes.buffer);
for(let pose=0;pose<3;pose++) dv.setFloat32((pose*128+pose)*4,1,true);
const requestId=randomUUID(), payload={
  request_id:requestId,expected_revision:0,purge_epoch:0,
  model_version:"sface_2021dec",
  embeddings_b64url:Buffer.from(bytes).toString("base64url"),
  consent:{policy_version:env.APPPOFO_FACE_POLICY_VERSION,
    storage_opt_in:true,discoverable_opt_in:false,
    confirmed_at:Date.now(),subject_present:true},
};
try {
  const inserted=await call("POST",payload);
  assert(inserted.status===201, "Synthetic upload failed (HTTP " + inserted.status + ")");
  assert(inserted.data?.data?.request_id===requestId &&
    inserted.data.data.revision===1,"Unexpected face commit acknowledgement");
  const receipt=await call("GET",undefined,"?request_id="+requestId);
  assert(receipt.status===200 && receipt.data?.data?.request_status==="COMMITTED" &&
    receipt.data.data.registered===true && receipt.data.data.revision===1,
    "Synthetic receipt verification failed");
  console.log("PASS: local Worker HTTP -> encrypted D1 -> revision+receipt verified.");
} finally {
  // Only delete data if the attempted write actually advanced this synthetic
  // profile to revision 1. Do not delete unrelated user biometric templates.
  const current=await call("GET");
  const ours=await call("GET",undefined,"?request_id="+requestId);
  if (current.status===200 && current.data?.data?.revision===1 &&
    current.data.data.registered===true && ours.status===200 &&
    ours.data?.data?.request_status==="COMMITTED" &&
    ours.data.data.request_revision===1) {
    const removed=await call("DELETE",{request_id:randomUUID(),
      expected_revision:1,purge_epoch:0});
    assert(removed.status===204,"Synthetic test face cleanup failed");
    console.log("PASS: local synthetic face removed, text profile retained.");
  }
}

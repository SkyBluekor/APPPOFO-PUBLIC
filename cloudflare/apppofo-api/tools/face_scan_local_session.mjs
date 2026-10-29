// S5-09-B: create an ephemeral SIGNED scanning session against localhost only.
// No real face template is enrolled or fetched. The new scanner text profile is
// intentionally local-only. The 30-minute bearer is saved in a gitignored file
// only after the caller explicitly approves local scan consent.
import { createHash, generateKeyPairSync, sign } from "node:crypto";
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { resolve } from "node:path";
import { parseLocalVars } from "./face_local_vars.mjs";

const CONFIRM = "--confirm-local-scan-consent";
const BASE = "http://127.0.0.1:8787";
const FILE = new URL("../.dev.vars.scan-session", import.meta.url);
const LOCAL_REQUEST_TIMEOUT_MS = 20_000;

// Read-only preflight: do not create another device if the Worker is not ready.
export async function checkLocalWorker(fetcher = fetch) {
  let response;
  try {
    response = await fetcher(BASE + "/api/health", {
      method:"GET", redirect:"error", signal:AbortSignal.timeout(5_000),
    });
  } catch {
    throw new Error("Local Worker health check timed out or was unreachable. " +
      "Check the Wrangler terminal at 127.0.0.1:8787 before creating devices.");
  }
  if (!response.ok)
    throw new Error("Local Worker health check failed (HTTP " + response.status + ").");
  return true;
}

async function post(path, data, bearer = null) {
  let response;
  try {
    response=await fetch(BASE+path,{
      method:"POST",redirect:"error",
      signal:AbortSignal.timeout(LOCAL_REQUEST_TIMEOUT_MS),
      headers:{
        "Content-Type":"application/json",
        ...(bearer ? {Authorization:"Bearer "+bearer} : {}),
      },
      body:JSON.stringify(data),
    });
  } catch (error) {
    // No credential, biometric, SQL or raw request/response data in errors.
    const detail = error?.name === "TimeoutError" || error?.name === "AbortError"
      ? "timed out after " + (LOCAL_REQUEST_TIMEOUT_MS/1000) + "s"
      : "could not reach the local Worker";
    throw new Error("Local signed-device request " + detail + " at " + path +
      ". Inspect the existing Wrangler terminal. Do not reset D1.");
  }
  // Do not echo headers, tokens or body on error.
  if (![200,201,204].includes(response.status))
    throw new Error("Local signed-device operation failed at "+path+
      " (HTTP "+response.status+")");
  return response.status===204 ? null : await response.json();
}

export async function createLocalScanSession(env, transport=post, onStage=()=>{}) {
  if (env?.APPPOFO_FACE_LOCAL_TEST_MODE!=="enabled" ||
      env.APPPOFO_FACE_MATCH_LOCAL_TEST_MODE!=="enabled" ||
      !env.APPPOFO_FACE_SCAN_POLICY_VERSION)
    throw new Error("Enable optional local match in start_face_local.ps1 first.");

  const pair=generateKeyPairSync("ec",{namedCurve:"prime256v1"});
  const source=pair.publicKey.export({format:"jwk"});
  const publicKey={kty:"EC",crv:"P-256",x:source.x,y:source.y};
  const thumbprint=createHash("sha256")
    .update("AppPofo-P256-v1\n"+source.x+"\n"+source.y)
    .digest("base64url");
  const init=await transport("/api/device/challenge",{
    purpose:"register",public_key:publicKey,
  });
  if (!init?.challenge_id) throw new Error("Missing local register challenge.");
  onStage("register-challenge");
  const registered=await transport("/api/device/register",{
    challenge_id:init.challenge_id,public_key:publicKey,
    signature:sign("sha256",Buffer.from(
      "AppPofo-v1\nregister\n"+init.challenge_id+"\n"+thumbprint),
      pair.privateKey).toString("base64url"),
  });
  const deviceId=registered?.device_id;
  if (!deviceId) throw new Error("No local device identity returned.");
  onStage("device-registration");

  const loginInit=await transport("/api/device/challenge",{
    purpose:"session",device_id:deviceId,
  });
  if (!loginInit?.challenge_id) throw new Error("Missing local session challenge.");
  onStage("session-challenge");
  const loggedIn=await transport("/api/device/session",{
    challenge_id:loginInit.challenge_id,device_id:deviceId,
    signature:sign("sha256",Buffer.from(
      "AppPofo-v1\nsession\n"+loginInit.challenge_id+"\n"+deviceId),
      pair.privateKey).toString("base64url"),
  });
  if (typeof loggedIn?.access_token!=="string" ||
      !/^appdev_[A-Za-z0-9_-]{43}$/.test(loggedIn.access_token) ||
      !Number.isSafeInteger(loggedIn.expires_at) ||
      loggedIn.expires_at<=Date.now())
    throw new Error("Invalid local signed-device session.");
  onStage("signed-session");

  // Register one synthetic TEXT profile so the scanner has a legitimate
  // linked active profile. Never upload a face or silently link another user.
  const scanner=await transport("/api/me/profiles",{
    display_name:"로컬 얼굴검색 테스트 기기",items:[],
  },loggedIn.access_token);
  if (!scanner?.data?.id) throw new Error("No local test scanner profile.");
  onStage("scanner-text-profile");

  await transport("/api/me/face/scan-consent",{
    consent:true,policy_version:env.APPPOFO_FACE_SCAN_POLICY_VERSION,
  },loggedIn.access_token);
  onStage("scan-consent");
  return {
    device_id:deviceId,
    access_token:loggedIn.access_token,
    expires_at:loggedIn.expires_at,
    scanner_profile_id:scanner.data.id,
    policy_version:env.APPPOFO_FACE_SCAN_POLICY_VERSION,
  };
}

if (process.argv[1] && resolve(process.argv[1])===fileURLToPath(import.meta.url)) {
  if (process.argv.length!==3 || process.argv[2]!==CONFIRM) {
    console.error("Usage: node tools/face_scan_local_session.mjs "+CONFIRM);
    process.exitCode=2;
  } else {
    try {
      const settings=parseLocalVars(readFileSync(new URL("../.dev.vars",import.meta.url),"utf8"));
      await checkLocalWorker();
      console.log("PASS: local Worker health responded; starting signed scanner setup.");
      const result=await createLocalScanSession(settings,post,stage=>
        console.log("PASS: local scanner stage " + stage));
      writeFileSync(FILE,JSON.stringify(result)+"\n",{encoding:"utf8",mode:0o600,flag:"w"});
      console.log("PASS: local signed scanner session and explicit scan consent created.");
      console.log("Unity Editor: Tools > AppPoFo > Local Face Scan Test");
      console.log("The short-lived token was stored only in ignored .dev.vars.scan-session.");
      console.log("No camera face, private embedding or production service was contacted.");
    } catch(err) {
      console.error(err instanceof Error?err.message:"Local scan setup failed.");
      process.exitCode=1;
    }
  }
}

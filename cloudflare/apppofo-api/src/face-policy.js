// Remote faces require an explicit environment, exact host and separate enable flag.
import { json, problem } from "./http.js";
import { decodeFaceBase64Url } from "./face-validation.js";
import { matchTestSettings } from "./face-match.js";
import { faceKeyFromEnv } from "./face-crypto.js";
export const PRODUCTION_FACE_HOST = "portfolio-api.example.invalid";
export function isLocalFaceHost(request) {
  const host = new URL(request.url).hostname;
  return host === "127.0.0.1" || host === "localhost" || host.endsWith(".test");
}
export function remoteFaceEnabled(request, env) {
  const url = new URL(request.url);
  if (url.protocol !== "https:" || isLocalFaceHost(request) ||
      env.APPPOFO_FACE_REMOTE_ENABLED !== "enabled" ||
      url.hostname !== env.APPPOFO_FACE_ALLOWED_HOST) return false;
  if (env.APPPOFO_DEPLOYMENT_STAGE === "production")
    return url.hostname === PRODUCTION_FACE_HOST;
  return env.APPPOFO_DEPLOYMENT_STAGE === "staging" && url.hostname !== PRODUCTION_FACE_HOST;
}
export function faceEnabled(request, env) {
  return remoteFaceEnabled(request, env) ||
    (isLocalFaceHost(request) && env.APPPOFO_FACE_LOCAL_TEST_MODE === "enabled");
}
export function scanEnabled(request, env) {
  return remoteFaceEnabled(request, env) ||
    (faceEnabled(request, env) && env.APPPOFO_FACE_MATCH_LOCAL_TEST_MODE === "enabled");
}
export async function faceConfiguration(request, env) {
  if (!remoteFaceEnabled(request, env))
    return problem(503, "FACE_NOT_READY", "서버 얼굴 기능이 아직 활성화되지 않았습니다.");
  try {
    decodeFaceBase64Url(faceKeyFromEnv(env, 1),32);
    const decision=matchTestSettings(env,true);
    await env.DB.prepare("SELECT 1 FROM face_scanner_authorizations LIMIT 1").first();
    await env.DB.prepare("SELECT 1 FROM face_mutation_rates LIMIT 1").first();
    await env.DB.prepare("SELECT consent_generation FROM face_consent_state LIMIT 1").first();
    if (!await env.DB.prepare("SELECT 1 FROM face_system_state WHERE singleton=1").first()) throw new Error("Schema unavailable");
    if (typeof env.APPPOFO_FACE_POLICY_VERSION !== "string" || !env.APPPOFO_FACE_POLICY_VERSION || env.APPPOFO_FACE_POLICY_VERSION.length>100 ||
        typeof env.APPPOFO_FACE_SCAN_POLICY_VERSION !== "string" || !env.APPPOFO_FACE_SCAN_POLICY_VERSION || env.APPPOFO_FACE_SCAN_POLICY_VERSION.length>100)
      throw new Error("Policy unavailable");
    return json({ enrollment_policy: env.APPPOFO_FACE_POLICY_VERSION,
      scan_policy: env.APPPOFO_FACE_SCAN_POLICY_VERSION, model_version: "sface_2021dec",
      decision_version: decision.decisionVersion, environment: env.APPPOFO_DEPLOYMENT_STAGE });
  } catch {
    return problem(503, "FACE_NOT_READY", "서버 얼굴 기능 설정을 확인해 주세요.");
  }
}

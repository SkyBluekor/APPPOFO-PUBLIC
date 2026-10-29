// S5-09-A deterministic math and strict one-vector wire contract.
// All thresholds are local synthetic-test settings, NOT calibrated identities.
import {
  FACE_MODEL_VERSION, decodeFaceBase64Url, validateFaceVectors, FaceInputError,
} from "./face-validation.js";

const ID = /^[a-f0-9]{8}-[a-f0-9]{4}-[1-8][a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/i;
const DIMENSIONS = 128;
const QUERY_BYTES = DIMENSIONS * 4;
const TEST_DECISION_VERSION = "local-synthetic-v1";
function fail(code, status = 400) { throw new FaceInputError(code, status); }

export function validateMatchRequest(body) {
  if (!body || typeof body !== "object" || Array.isArray(body) ||
      Object.keys(body).sort().join(",") !==
        "embedding_b64url,model_version,request_id")
    fail("INVALID_FACE_MATCH_BODY");
  if (new TextEncoder().encode(JSON.stringify(body)).length > 2048)
    fail("FACE_MATCH_BODY_TOO_LARGE", 413);
  if (!ID.test(body.request_id ?? "")) fail("INVALID_FACE_MATCH_REQUEST_ID");
  if (body.model_version !== FACE_MODEL_VERSION)
    fail("UNSUPPORTED_FACE_MODEL", 422);
  const bytes = decodeFaceBase64Url(body.embedding_b64url, QUERY_BYTES);
  validateUnitVector(bytes);
  return bytes;
}

export function validateUnitVector(bytes) {
  if (!(bytes instanceof Uint8Array) || bytes.byteLength !== QUERY_BYTES)
    fail("INVALID_FACE_MATCH_VECTOR");
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let squared = 0;
  for (let i = 0; i < DIMENSIONS; i++) {
    const value = view.getFloat32(i * 4, true);
    if (!Number.isFinite(value) || Math.abs(value) > 1)
      fail("INVALID_FACE_MATCH_VECTOR");
    squared += value * value;
  }
  if (Math.abs(Math.sqrt(squared) - 1) > .001)
    fail("INVALID_FACE_MATCH_NORM");
  return bytes;
}

export function matchTestSettings(env, remote=false) {
  const thresholdText = (remote ? env?.APPPOFO_FACE_MATCH_THRESHOLD : env?.APPPOFO_FACE_MATCH_THRESHOLD_TEST);
  const marginText = (remote ? env?.APPPOFO_FACE_MATCH_MARGIN : env?.APPPOFO_FACE_MATCH_MARGIN_TEST);
  const decimal = /^(?:0(?:\.\d+)?|1(?:\.0+)?)$/;
  if (typeof thresholdText !== "string" || !decimal.test(thresholdText) ||
      typeof marginText !== "string" || !decimal.test(marginText))
    fail("FACE_MATCH_POLICY_NOT_CONFIGURED", 503);
  const threshold = Number(thresholdText), margin = Number(marginText);
  // Staging comparison permits 0.65; deployment settings still default to 0.75.
  if (threshold < .5 || threshold > 1 || margin < 0 || margin > .5 ||
      (remote && (threshold < .65 || margin < .05)))
    fail("FACE_MATCH_POLICY_NOT_CONFIGURED", 503);
  return { threshold, margin, decisionVersion: remote
    ? (env.APPPOFO_DEPLOYMENT_STAGE === "production" ? "production-v1" : "staging-unvalidated-v1")
    : TEST_DECISION_VERSION };
}

// One query compared with three normalized pose embeddings for ONE candidate.
// All bytes are used only in memory, never logged or returned to clients.
export function bestPoseScore(query, plainThreePoses) {
  validateUnitVector(query);
  validateFaceVectors(plainThreePoses);
  const q = new DataView(query.buffer, query.byteOffset, query.byteLength);
  const poses = new DataView(plainThreePoses.buffer,
    plainThreePoses.byteOffset, plainThreePoses.byteLength);
  let best = -Infinity;
  for (let pose = 0; pose < 3; pose++) {
    let dot = 0;
    for (let i = 0; i < DIMENSIONS; i++)
      dot += q.getFloat32(i * 4, true) *
        poses.getFloat32((pose * DIMENSIONS + i) * 4, true);
    if (!Number.isFinite(dot)) fail("INVALID_FACE_MATCH_SCORE", 503);
    best = Math.max(best, Math.max(-1, Math.min(1, dot)));
  }
  return best;
}

export function chooseMatch(candidates, {threshold, margin}) {
  if (!Array.isArray(candidates) || candidates.some(x =>
      typeof x.id !== "string" || !Number.isFinite(x.score)))
    fail("INVALID_FACE_MATCH_CANDIDATES", 503);
  const ranked = [...candidates].sort((a,b) => b.score - a.score ||
    a.id.localeCompare(b.id));
  if (!ranked.length || ranked[0].score < threshold ||
      (ranked.length > 1 && ranked[0].score - ranked[1].score < margin))
    return null;
  return ranked[0];
}

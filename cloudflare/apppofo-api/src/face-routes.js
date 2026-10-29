import { faceEnabled, remoteFaceEnabled } from "./face-policy.js";
import { chargeFaceMutation, withdrawDiscovery } from "./face-staging-operations.js";
// S5-08-B face-specific transport. Explicit development gate prevents an
// accidentally deployed branch from accepting biometrics before rollout approval.
import { json, problem, noContent, readJson } from "./http.js";
import { requireFaceProfileAccess, FaceAccessError } from "./face-access.js";
import { FaceInputError } from "./face-validation.js";
import { FaceCryptoError } from "./face-crypto.js";
import { FaceStoreError, faceStatus, enrollFace, deleteFace } from "./face-repository.js";

export async function handleFaceRoute(request, env, profileId, actor) {
  if (!["GET","POST","DELETE","PATCH"].includes(request.method) ||
      (request.method === "PATCH" && !remoteFaceEnabled(request,env)))
    return problem(405,"METHOD_NOT_ALLOWED","지원하지 않는 메서드입니다.");
  try {
    await requireFaceProfileAccess(env.DB, profileId, actor);
  } catch (error) {
    if (error instanceof FaceAccessError)
      return problem(error.status,error.code,"얼굴 등록 대상에 접근할 수 없습니다.");
    throw error;
  }
  // This flag is intentionally NOT configured in wrangler.jsonc. Local test
  // fixtures must explicitly opt in with synthetic faces and ephemeral D1.
  if (!faceEnabled(request,env))
    return problem(503,"FACE_ENROLLMENT_NOT_READY",
      "서버 얼굴 등록 기능이 아직 준비되지 않았습니다. 얼굴정보는 전송·저장되지 않습니다.");

  try {
    const url = new URL(request.url);
    if (request.method === "GET") {
      const entries = [...url.searchParams.keys()];
      if (entries.some(k=>k!=="request_id") || entries.length>1)
        return problem(400,"INVALID_FACE_QUERY","잘못된 상태 조회 요청입니다.");
      const requestId = url.searchParams.get("request_id");
      const data = await faceStatus(env.DB,profileId,actor,requestId,remoteFaceEnabled(request,env));
      return json({data});
    }
    if (remoteFaceEnabled(request,env) && request.method === "POST") await chargeFaceMutation(env.DB,actor);
    if (request.method === "PATCH") return await withdrawDiscovery(request,env,profileId,actor);
    const body = await readJson(request);
    if (body.error) return body.error;
    if (request.method === "DELETE") {
      await deleteFace(env.DB,profileId,actor,body.value);
      return noContent();
    }
    const profile = await requireFaceProfileAccess(env.DB,profileId,actor,
      {requireActive:true});
    if (!profile.active)
      return problem(409,"INACTIVE_PROFILE","비활성 프로필은 얼굴을 등록할 수 없습니다.");
    let generation=null;
    if (remoteFaceEnabled(request,env)) {
      const header=request.headers.get("If-Face-Consent-Generation");
      if (!/^(0|[1-9][0-9]{0,9})$/.test(header??"") || Number(header)>2147483647)
        return problem(400,"FACE_CONSENT_GENERATION_REQUIRED","현재 동의 상태를 다시 확인해 주세요.");
      generation=Number(header);
    }
    const data = await enrollFace(env.DB,env,profileId,actor,body.value,generation);
    const status = data.replayed ? 200 : data.revision===1 ? 201 : 200;
    return json({data},status);
  } catch (error) {
    if (error instanceof FaceAccessError)
      return problem(error.status,error.code,"해당 프로필에서 얼굴을 등록할 수 없습니다.");
    if (error instanceof FaceInputError || error instanceof FaceStoreError)
      return problem(error.status,error.code,"얼굴 등록 상태 또는 요청을 확인해 주세요.");
    if (error instanceof FaceCryptoError)
      return problem(503,error.code,"얼굴 암호화 설정을 확인할 수 없습니다.");
    // Database errors and secrets must never reach clients or biometric logs.
    console.error("[FaceStorage] request failed",error instanceof Error ? error.name:"Unknown");
    return problem(500,"FACE_STORAGE_ERROR","얼굴정보를 처리하지 못했습니다.");
  }
}

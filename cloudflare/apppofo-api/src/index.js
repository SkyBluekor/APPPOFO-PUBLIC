import { faceConfiguration, faceEnabled } from "./face-policy.js";
import { authorizeScanner } from "./face-staging-operations.js";
import { createProfileOnce } from "./profile-idempotency.js";
import { json, problem, noContent, readJson } from "./http.js";
import { InputError, parsePage, validateProfile, validCloudId } from "./validation.js";
import { deleteProfile, listProfiles, updateProfile } from "./repository.js";
import { getProfileForLearning } from "./student-get.js";
import { handleDeviceRoute, authenticatedDevice } from "./device-auth.js";
import { listMine, getMine, updateMine, deleteMine } from "./mobile-repository.js";
import { issueProfileCode, connectProfileByCode, listProfileDevices, revokeProfileDevice } from "./profile-links.js";
import { handleFaceRoute } from "./face-routes.js";
import { handleLocalFaceScan } from "./face-scan-routes.js";
import { prepareFacePurge, confirmFacePurge } from "./face-purge.js";
import { FaceStoreError } from "./face-repository.js";
import { attachProfileAvatars, profileResponseWithAvatar, saveProfileAvatar } from "./profile-avatars.js";

const encoder = new TextEncoder();
async function equalSecret(actual, expected) {
  // Compare digests in fixed time; don't terminate on the first differing byte.
  const a = new Uint8Array(await crypto.subtle.digest("SHA-256", encoder.encode(actual)));
  const b = new Uint8Array(await crypto.subtle.digest("SHA-256", encoder.encode(expected)));
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a[i] ^ b[i];
  return diff === 0;
}

export default {
  async fetch(request, env) {
    try {
      // Public connectivity probe exposes no profiles, credentials or database metadata.
      // Mobile profile routes remain protected until per-device ownership is implemented.
      const requestUrl = new URL(request.url);
      if (requestUrl.pathname === "/api/health" && request.method === "GET") {
        return env?.DB ? json({ status: "ok" }) :
          problem(503, "SERVICE_NOT_CONFIGURED", "서버 설정이 완료되지 않았습니다.");
      }
      // Fail closed, including for the list GET endpoint. No public personal-data API.
      if (!env?.DB || typeof env.APPPOFO_API_TOKEN !== "string" ||
          env.APPPOFO_API_TOKEN.length < 32) {
        return problem(503, "SERVICE_NOT_CONFIGURED", "서버 설정이 완료되지 않았습니다.");
      }
      // Device routes never accept the all-powerful administrator token.
      const mobilePath = requestUrl.pathname.replace(/\/$/, "") || "/";
      if (/^\/api\/device\/(challenge|register|session)$/.test(mobilePath)) {
        return handleDeviceRoute(request, env.DB, mobilePath);
      }
      if (mobilePath === "/api/me" || mobilePath.startsWith("/api/me/")) {
        const owner = await authenticatedDevice(env.DB, request);
        if (!owner) return problem(401, "UNAUTHORIZED", "기기 인증이 필요합니다.");
        if (mobilePath === "/api/me" && request.method === "GET") {
          return json({ device_id: owner });
        }
        if (mobilePath === "/api/me/logout" && request.method === "POST") {
          // Revoke the current session only; never delete the permanent device key.
          const token = request.headers.get("authorization").slice(7);
          const bytes = new Uint8Array(await crypto.subtle.digest("SHA-256", encoder.encode(token)));
          let binary = "";
          for (const byte of bytes) binary += String.fromCharCode(byte);
          const hash = btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
          await env.DB.prepare("UPDATE device_sessions SET revoked_at=? WHERE token_hash=? AND device_id=?")
            .bind(Date.now(), hash, owner).run();
          return noContent();
        }
        if (mobilePath === "/api/me/profiles") {
          if (request.method === "GET") {
            const page = await listMine(env.DB, owner, parsePage(requestUrl.searchParams));
            page.data = await attachProfileAvatars(env.DB, page.data);
            return json(page);
          }
          if (request.method === "POST") {
            const body = await readJson(request);
            if (body.error) return body.error;
            return json({ data: await createProfileOnce(env.DB, owner, validateProfile(body.value), request.headers.get("Idempotency-Key")) }, 201);
          }
          return problem(405, "METHOD_NOT_ALLOWED", "지원하지 않는 메서드입니다.");
        }
        if (mobilePath === "/api/me/profile-links" && request.method === "POST")
          return connectProfileByCode(request, env.DB, owner);
        const issueMatch = /^\/api\/me\/profiles\/([^/]+)\/link-code$/.exec(mobilePath);
        if (issueMatch && request.method === "POST")
          return issueProfileCode(env.DB, issueMatch[1], owner);
        const deviceListMatch = /^\/api\/me\/profiles\/([^/]+)\/devices$/.exec(mobilePath);
        if (deviceListMatch && request.method === "GET")
          return listProfileDevices(env.DB, deviceListMatch[1], owner);
        const deviceRevokeMatch = /^\/api\/me\/profiles\/([^/]+)\/devices\/([^/]+)$/.exec(mobilePath);
        if (deviceRevokeMatch && request.method === "DELETE")
          return revokeProfileDevice(env.DB, deviceRevokeMatch[1], deviceRevokeMatch[2], owner);
        // Device-only scanner endpoints. Policy selects explicit local or staging
        // gates; a shared administrator bearer never acts as the scanner.
        if (mobilePath === "/api/me/face/config" && request.method === "GET")
          return faceConfiguration(request,env);
        if (mobilePath === "/api/me/face/scan-consent")
          return handleLocalFaceScan(request,env,owner,"consent");
        if (mobilePath === "/api/me/face/match")
          return handleLocalFaceScan(request,env,owner,"match");
        const publicSummaryMatch = /^\/api\/me\/face\/profiles\/([^/]+)\/summary$/.exec(mobilePath);
        if (publicSummaryMatch) return handleLocalFaceScan(request,env,owner,"summary",publicSummaryMatch[1]);
        const faceMatch = /^\/api\/me\/profiles\/([^/]+)\/face$/.exec(mobilePath);
        if (faceMatch)
          return handleFaceRoute(request, env, faceMatch[1],
            { kind: "device", deviceId: owner });
        const avatarMatch = /^\/api\/me\/profiles\/([^/]+)\/avatar$/.exec(mobilePath);
        if (avatarMatch) return saveProfileAvatar(request, env.DB, avatarMatch[1], owner);
        const mineMatch = /^\/api\/me\/profiles\/([^/]+)$/.exec(mobilePath);
        if (!mineMatch) return problem(404, "NOT_FOUND", "주소를 찾을 수 없습니다.");
        const id = mineMatch[1];
        if (!validCloudId(id)) return problem(400, "INVALID_ID", "유효하지 않은 클라우드 ID입니다.");
        if (request.method === "GET") return profileResponseWithAvatar(env.DB, await getMine(env.DB, owner, id));
        if (request.method === "PUT") {
          const body = await readJson(request);
          if (body.error) return body.error;
          const result = await updateMine(env.DB, owner, id, validateProfile(body.value));
          return result ? json({ data: result }) : problem(404, "NOT_FOUND", "프로필이 없습니다.");
        }
        if (request.method === "DELETE") return deleteMine(env.DB, owner, id);
        return problem(405, "METHOD_NOT_ALLOWED", "지원하지 않는 메서드입니다.");
      }
      // Legacy administrator endpoints remain private.
      const header = request.headers.get("authorization") ?? "";
      const token = /^Bearer ([^\s]+)$/.exec(header)?.[1];
      if (!token || !(await equalSecret(token, env.APPPOFO_API_TOKEN))) {
        return problem(401, "UNAUTHORIZED", "인증이 필요합니다.");
      }
      const url = new URL(request.url);
      const path = url.pathname.replace(/\/$/, "") || "/";
      if (path === "/api/face/config" && request.method === "GET") return faceConfiguration(request,env);
      if (path === "/api/admin/face-scanners") return authorizeScanner(request,env);
      if (/^\/api\/admin\/face-templates\/purge\/(prepare|confirm)$/.test(path)) {
        if (request.method !== "POST")
          return problem(405,"METHOD_NOT_ALLOWED","지원하지 않는 메서드입니다.");
        if (!faceEnabled(request,env))
          return problem(503,"FACE_ENROLLMENT_NOT_READY","얼굴정보 저장 기능이 준비되지 않았습니다.");
        try {
          if (path.endsWith("/prepare"))
            return json({data:await prepareFacePurge(env.DB)},201);
          const body = await readJson(request);
          if (body.error) return body.error;
          return json({data:await confirmFacePurge(env.DB,body.value)});
        } catch (error) {
          if (error instanceof FaceStoreError)
            return problem(error.status,error.code,"얼굴정보 삭제 확인을 다시 진행해 주세요.");
          console.error("[FacePurge] failed",error instanceof Error?error.name:"Unknown");
          return problem(500,"FACE_PURGE_ERROR","얼굴정보 삭제를 완료하지 못했습니다.");
        }
      }
      if (path === "/api/profiles") {
        if (request.method === "GET") {
          const page = await listProfiles(env.DB, parsePage(url.searchParams));
          page.data = await attachProfileAvatars(env.DB, page.data);
          return json(page);
        }
        if (request.method === "POST") {
          const body = await readJson(request);
          if (body.error) return body.error;
          return json({ data: await createProfileOnce(env.DB, null, validateProfile(body.value), request.headers.get("Idempotency-Key")) }, 201);
        }
        return problem(405, "METHOD_NOT_ALLOWED", "지원하지 않는 메서드입니다.");
      }
      const adminCodeMatch = /^\/api\/profiles\/([^/]+)\/link-code$/.exec(path);
      if (adminCodeMatch && request.method === "POST")
        return issueProfileCode(env.DB, adminCodeMatch[1]);
      const adminFaceMatch = /^\/api\/profiles\/([^/]+)\/face$/.exec(path);
      if (adminFaceMatch)
        return handleFaceRoute(request, env, adminFaceMatch[1],
          { kind: "admin" });
      const adminAvatarMatch = /^\/api\/profiles\/([^/]+)\/avatar$/.exec(path);
      if (adminAvatarMatch) return saveProfileAvatar(request, env.DB, adminAvatarMatch[1]);
      const match = /^\/api\/profiles\/([^/]+)$/.exec(path);
      if (!match) return problem(404, "NOT_FOUND", "주소를 찾을 수 없습니다.");
      const id = match[1];
      if (!validCloudId(id)) return problem(400, "INVALID_ID", "유효하지 않은 클라우드 ID입니다.");
      if (request.method === "GET") return profileResponseWithAvatar(env.DB, await getProfileForLearning(env, id));
      if (request.method === "PUT") {
        const body = await readJson(request);
        if (body.error) return body.error;
        const result = await updateProfile(env.DB, id, validateProfile(body.value));
        return result ? json({ data: result }) : problem(404, "NOT_FOUND", "프로필이 없습니다.");
      }
      if (request.method === "DELETE") {
        return await deleteProfile(env.DB, id) ? noContent() : problem(404, "NOT_FOUND", "프로필이 없습니다.");
      }
      return problem(405, "METHOD_NOT_ALLOWED", "지원하지 않는 메서드입니다.");
    } catch (error) {
      if (error instanceof FaceStoreError) return problem(error.status,error.code,"저장 요청 또는 현재 권한을 확인해 주세요.");
      if (error instanceof InputError) return problem(400, "INVALID_INPUT", error.message);
      // SQL details and personal data must not leak in HTTP responses.
      console.error("[S5-03] Request failed", error instanceof Error ? error.name : "UnknownError");
      return problem(500, "INTERNAL_ERROR", "서버 요청을 처리할 수 없습니다.");
    }
  },
};

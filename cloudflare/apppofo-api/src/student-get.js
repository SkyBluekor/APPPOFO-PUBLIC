import { json, problem } from "./http.js";

/**
 * 직접 학습 과제 B: GET /api/profiles/:id
 * 본 함수만 사용자가 구현합니다. 나머지 서버/검증은 AI가 관리합니다.
 * Authorization 검사와 UUID 경로 검증은 index.js에서 이미 처리합니다.
 * 요구사항: DB.prepare(...).bind(id), 프로필과 SKILL/PROJECT/LINK 조회,
 * 없는 ID는 404, 있으면 {data: { ...profile, items: [...] }} JSON 200.
 * SQL은 물음표 바인딩을 사용하고 face_embeddings/avatar_path는 조회하지 않습니다.
 */
export async function getProfileForLearning(env, id) {
  const profile = await env.DB.prepare(
    "SELECT * FROM profiles where id = ?"
  ).bind(id).first();

  if(profile == null){
    return problem(404,"NOT_FOUND","프로필이 없습니다.");
  }

  const result = await env.DB.prepare(
    "SELECT * FROM profile_items WHERE profile_id = ? ORDER BY sort_order, id"
  ).bind(id).all();

  const items = result.results;
  return json({
    data:{ ...profile, items: items}
  });
}

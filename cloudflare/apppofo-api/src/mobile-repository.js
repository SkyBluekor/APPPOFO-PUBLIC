import { json, problem, noContent } from "./http.js";

const authorized="EXISTS(SELECT 1 FROM profile_devices pd WHERE pd.profile_id=profiles.id AND pd.device_id=? AND pd.revoked_at IS NULL)";
function insertItems(db,id,items,owner=null) {
  return items.map(item=>db.prepare(owner===null
    ? "INSERT INTO profile_items(profile_id,item_type,title,description,url,sort_order) VALUES(?,?,?,?,?,?)"
    : "INSERT INTO profile_items(profile_id,item_type,title,description,url,sort_order) SELECT ?,?,?,?,?,? WHERE EXISTS(SELECT 1 FROM profile_devices WHERE profile_id=? AND device_id=? AND revoked_at IS NULL)"
  ).bind(...(owner===null
    ? [id,item.item_type,item.title,item.description,item.url,item.sort_order]
    : [id,item.item_type,item.title,item.description,item.url,item.sort_order,id,owner])));
}
export async function listMine(db,owner,page) {
  const [count,results]=await db.batch([
    db.prepare("SELECT COUNT(*) total FROM profiles WHERE "+authorized).bind(owner),
    db.prepare("SELECT id,display_name,headline,business_card_theme,updated_at FROM profiles WHERE "+authorized+" ORDER BY created_at DESC,id DESC LIMIT ? OFFSET ?").bind(owner,page.limit,page.offset)
  ]);
  return {data:results.results,total:count.results[0].total,...page};
}
export async function createMine(db,owner,profile,options={}) {
  const id=options.id??crypto.randomUUID(),now=options.now??Date.now();
  await db.batch([
    db.prepare("INSERT INTO profiles(id,display_name,headline,bio,email,website,business_card_theme,is_active,created_at,updated_at,owner_device_id) VALUES(?,?,?,?,?,?,?,?,?,?,?)")
      .bind(id,profile.display_name,profile.headline,profile.bio,profile.email,profile.website,profile.business_card_theme,profile.is_active,now,now,owner),
    db.prepare("INSERT INTO profile_devices(profile_id,device_id,granted_at) VALUES(?,?,?)").bind(id,owner,now),
    ...insertItems(db,id,profile.items),
    ...(options.receipt?[options.receipt]:[])
  ]);
  return {id,created_at:now,updated_at:now};
}
export async function getMine(db,owner,id) {
  const p=await db.prepare("SELECT id,display_name,headline,bio,email,website,business_card_theme,is_active,created_at,updated_at FROM profiles WHERE id=? AND "+authorized).bind(id,owner).first();
  if(!p) return problem(404,"NOT_FOUND","프로필이 없습니다.");
  const items=await db.prepare("SELECT item_type,title,description,url,sort_order FROM profile_items WHERE profile_id=? ORDER BY sort_order,id").bind(id).all();
  return json({data:{...p,items:items.results}});
}
export async function updateMine(db,owner,id,profile) {
  const exists=await db.prepare("SELECT id FROM profiles WHERE id=? AND "+authorized).bind(id,owner).first();
  if(!exists)return null;
  const now=Date.now();
  await db.batch([
    db.prepare("UPDATE profiles SET display_name=?,headline=?,bio=?,email=?,website=?,business_card_theme=?,is_active=?,updated_at=? WHERE id=? AND "+authorized)
      .bind(profile.display_name,profile.headline,profile.bio,profile.email,profile.website,profile.business_card_theme,profile.is_active,now,id,owner),
    db.prepare("DELETE FROM profile_items WHERE profile_id=? AND EXISTS(SELECT 1 FROM profile_devices WHERE profile_id=? AND device_id=? AND revoked_at IS NULL)").bind(id,id,owner),
    ...insertItems(db,id,profile.items,owner)
  ]);
  return {id,updated_at:now};
}
export async function deleteMine(db,owner,id) {
  const result=await db.prepare("DELETE FROM profiles WHERE id=? AND "+authorized).bind(id,owner).run();
  return (result.meta?.changes??0)>0?noContent():problem(404,"NOT_FOUND","프로필이 없습니다.");
}

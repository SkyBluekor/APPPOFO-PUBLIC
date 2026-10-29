import test from "node:test";
import { generateKeyPairSync, sign, createHash } from "node:crypto";
import assert from "node:assert/strict";
import { DatabaseSync } from "node:sqlite";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import worker from "../src/index.js";

const migration = ["0001_profiles.sql","0002_device_ownership.sql","0003_profile_connections.sql","0007_profile_avatars.sql"]
  .map(file => readFileSync(fileURLToPath(new URL("../migrations/"+file,import.meta.url)),"utf8")).join("\n");
function fixture() {
  const db = new DatabaseSync(":memory:");
  db.exec("PRAGMA foreign_keys=ON;");
  db.exec(migration);
  const createStmt = (sql, params = []) => ({
    bind: (...args) => createStmt(sql,args),
    first: async () => db.prepare(sql).get(...params) ?? null,
    all: async () => ({ results: db.prepare(sql).all(...params) }),
    run: async () => sql.trimStart().startsWith("SELECT")
      ? {results: db.prepare(sql).all(...params)}
      : {results:[],meta:{changes:Number(db.prepare(sql).run(...params).changes)}}
  });
  const DB = { prepare: sql => createStmt(sql), async batch(statements) {
    db.exec("BEGIN;");
    try {
      const results=[];
      for(const statement of statements) results.push(await statement.run());
      db.exec("COMMIT;");
      return results;
    } catch(error) { db.exec("ROLLBACK;"); throw error; }
  }};
  const secret = Array.from({length:5},()=>crypto.randomUUID()).join("-");
  const env = {DB, APPPOFO_API_TOKEN:secret};
  const call = (path,method="GET",payload,authenticated=true) => worker.fetch(
    new Request("https://example.invalid"+path,{
      method,
      headers:{
        ...(authenticated?{Authorization:"Bearer "+(typeof authenticated==="string"?authenticated:secret)}:{}),
        ...(payload===undefined?{}:{"Content-Type":"application/json"})
      },
      ...(payload===undefined?{}:{body:JSON.stringify(payload)})
    }),env);
  return {call,env,db};
}

test("private endpoint blocks missing credentials and missing configuration", async () => {
  const f=fixture();
  assert.equal((await f.call("/api/profiles","GET",undefined,false)).status,401);
  delete f.env.APPPOFO_API_TOKEN;
  assert.equal((await f.call("/api/profiles")).status,503);
  f.db.close();
});

test("create, list, replace and delete cloud-only profiles", async () => {
  const f=fixture();
  const r=await f.call("/api/profiles","POST",{display_name:"홍길동",items:[{item_type:"SKILL",title:"C#"}]});
  assert.equal(r.status,201);
  const {id}=(await r.json()).data;
  assert.equal((await (await f.call("/api/profiles")).json()).total,1);
  assert.equal((await f.call("/api/profiles/"+id,"PUT",{display_name:"변경",items:[]})).status,200);
  assert.equal(f.db.prepare("SELECT COUNT(*) n FROM profile_items").get().n,0);
  assert.equal((await f.call("/api/profiles/"+id,"DELETE")).status,204);
  assert.equal(f.db.prepare("SELECT COUNT(*) n FROM profiles").get().n,0);
  f.db.close();
});

test("student GET: profile with SKILL/PROJECT/LINK returns 200, ordered and isolated", async () => {
  const f = fixture();
  try {
    const created = await f.call("/api/profiles", "POST", {
      display_name: "홍길동",
      headline: "Unity 개발자",
      email: "demo@example.invalid",
      items: [
        { item_type: "PROJECT", title: "AppPofo", sort_order: 2 },
        { item_type: "LINK", title: "GitHub", sort_order: 0 },
        { item_type: "SKILL", title: "C#", sort_order: 1 },
      ],
    });
    assert.equal(created.status, 201);
    const id = (await created.json()).data.id;
    const other = await f.call("/api/profiles", "POST", {
      display_name: "다른 사용자",
      items: [{ item_type: "SKILL", title: "Python", sort_order: 0 }],
    });
    assert.equal(other.status, 201);

    const response = await f.call("/api/profiles/" + id);
    assert.equal(response.status, 200);
    const { data } = await response.json();
    assert.equal(data.id, id);
    assert.equal(data.display_name, "홍길동");
    assert.equal(data.headline, "Unity 개발자");
    assert.equal(data.email, "demo@example.invalid");
    assert.deepEqual(data.items.map((item) => [item.item_type, item.title]), [
      ["LINK", "GitHub"],
      ["SKILL", "C#"],
      ["PROJECT", "AppPofo"],
    ]);
    assert.equal(data.items.every((item) => item.profile_id === id), true);
    assert.equal("face_embeddings" in data, false);
    assert.equal("avatar_path" in data, false);
    assert.equal("embedding_blob" in data, false);
  } finally {
    f.db.close();
  }
});

test("student GET: nonexistent valid UUID returns 404 with stable error", async () => {
  const f = fixture();
  try {
    const response = await f.call("/api/profiles/11111111-1111-4111-8111-111111111111");
    assert.equal(response.status, 404);
    assert.equal((await response.json()).error.code, "NOT_FOUND");
  } finally {
    f.db.close();
  }
});

test("student GET: authentication and UUID validation remain enforced", async () => {
  const f = fixture();
  try {
    const uuid = "11111111-1111-4111-8111-111111111111";
    assert.equal((await f.call("/api/profiles/" + uuid, "GET", undefined, false)).status, 401);
    assert.equal((await f.call("/api/profiles/not-a-uuid")).status, 400);
  } finally {
    f.db.close();
  }
});

test("public health exposes only availability, not private profiles", async () => {
  const f = fixture();
  try {
    const response = await f.call("/api/health", "GET", undefined, false);
    assert.equal(response.status, 200);
    assert.deepEqual(await response.json(), { status: "ok" });
    assert.equal((await f.call("/api/profiles", "GET", undefined, false)).status, 401);
    delete f.env.DB;
    assert.equal((await f.call("/api/health", "GET", undefined, false)).status, 503);
  } finally {
    f.db.close();
  }
});

function newKeyPair() {
  const pair=generateKeyPairSync("ec",{namedCurve:"prime256v1"});
  const exported=pair.publicKey.export({format:"jwk"});
  return {privateKey:pair.privateKey, jwk:{
    kty:"EC", crv:"P-256", x:exported.x, y:exported.y
  }};
}
function proof(privateKey,purpose,id,subject) {
  return sign("sha256",Buffer.from("AppPofo-v1\n"+purpose+"\n"+id+"\n"+subject),
    privateKey).toString("base64url");
}
function fingerprint(key) {
  return createHash("sha256").update("AppPofo-P256-v1\n"+key.x+"\n"+key.y).digest("base64url");
}
async function enroll(call,pair) {
  const chal=await call("/api/device/challenge","POST",
    {purpose:"register",public_key:pair.jwk},false);
  assert.equal(chal.status,201);
  const id=(await chal.json()).challenge_id;
  const signed=proof(pair.privateKey,"register",id,fingerprint(pair.jwk));
  const reg=await call("/api/device/register","POST",
    {challenge_id:id,public_key:pair.jwk,signature:signed},false);
  assert.equal(reg.status,201);
  const deviceId=(await reg.json()).device_id;
  assert.equal((await call("/api/device/register","POST",
    {challenge_id:id,public_key:pair.jwk,signature:signed},false)).status,401,
    "Registration challenge must be used once.");
  const sessionChal=await call("/api/device/challenge","POST",
    {purpose:"session",device_id:deviceId},false);
  assert.equal(sessionChal.status,201);
  const sessionId=(await sessionChal.json()).challenge_id;
  const sessionSignature=proof(pair.privateKey,"session",sessionId,deviceId);
  const session=await call("/api/device/session","POST",
    {challenge_id:sessionId,device_id:deviceId,signature:sessionSignature},false);
  assert.equal(session.status,200);
  const token=(await session.json()).access_token;
  assert.match(token,/^appdev_[A-Za-z0-9_-]{43}$/);
  assert.equal((await call("/api/device/session","POST",
    {challenge_id:sessionId,device_id:deviceId,signature:sessionSignature},false)).status,401,
    "Session challenge must be used once.");
  return {deviceId,token};
}
test("S5-04B: per-device authorization protects cloud CRUD and legacy profiles", async () => {
  const f=fixture();
  try {
    const a=await enroll(f.call,newKeyPair());
    const b=await enroll(f.call,newKeyPair());
    assert.equal((await f.call("/api/me","GET",undefined,a.token)).status,200);
    assert.equal((await f.call("/api/me","GET",undefined,false)).status,401);
    assert.equal((await f.call("/api/me","GET")).status,401,
      "Administrator token must not be accepted as device identity.");

    const legacy=await f.call("/api/profiles","POST",{display_name:"관리자",items:[]});
    assert.equal(legacy.status,201);
    const created=await f.call("/api/me/profiles","POST",
      {display_name:"기기 A",items:[{item_type:"SKILL",title:"Unity"}]},a.token);
    assert.equal(created.status,201);
    const id=(await created.json()).data.id;
    const own=await f.call("/api/me/profiles","GET",undefined,a.token);
    assert.equal((await own.json()).total,1,"Admin rows must never become device-owned.");
    assert.equal((await (await f.call("/api/me/profiles","GET",undefined,b.token)).json()).total,0);
    const adminRows=(await (await f.call("/api/profiles","GET")).json()).data;
    const adminRow=adminRows.find(row=>row.display_name==="관리자");
    const mobileRow=adminRows.find(row=>row.display_name==="기기 A");
    assert.equal(adminRow.ownership,"ADMIN","Administrator-created profile must be labelled.");
    assert.equal(mobileRow.ownership,"DEVICE","Device-owned profile must be labelled.");
    assert.equal("owner_device_id" in mobileRow,false,"Do not reveal device ID in list.");

    assert.equal((await f.call("/api/me/profiles/"+id,"GET",undefined,b.token)).status,404);
    assert.equal((await f.call("/api/me/profiles/"+id,"PUT",
      {display_name:"탈취",items:[]},b.token)).status,404);
    assert.equal((await f.call("/api/me/profiles/"+id,"DELETE",undefined,b.token)).status,404);
    assert.equal((await f.call("/api/me/profiles/"+id,"GET",undefined,false)).status,401);

    const mine=await f.call("/api/me/profiles/"+id,"GET",undefined,a.token);
    assert.equal(mine.status,200);
    const entry=(await mine.json()).data;
    assert.equal(entry.display_name,"기기 A");
    assert.equal("owner_device_id" in entry,false);
    assert.equal(entry.items[0].title,"Unity");
    assert.equal((await f.call("/api/me/profiles/"+id,"PUT",
      {display_name:"수정",items:[]},a.token)).status,200);
    assert.equal((await f.call("/api/me/profiles/"+id,"DELETE",undefined,a.token)).status,204);
    assert.equal((await f.call("/api/me/profiles/"+id,"GET",undefined,a.token)).status,404);

    assert.equal((await f.call("/api/me/logout","POST",undefined,b.token)).status,204);
    assert.equal((await f.call("/api/me","GET",undefined,b.token)).status,401);
    assert.equal((await f.call("/api/profiles","GET",undefined,b.token)).status,401);
  } finally { f.db.close(); }
});
test("S5-04B: forged signature and disallowed mobile fields are rejected", async () => {
  const f=fixture();
  try {
    const pair=newKeyPair(),other=newKeyPair();
    const challenge=await f.call("/api/device/challenge","POST",
      {purpose:"register",public_key:pair.jwk},false);
    const id=(await challenge.json()).challenge_id;
    const bad=proof(other.privateKey,"register",id,fingerprint(pair.jwk));
    assert.equal((await f.call("/api/device/register","POST",
      {challenge_id:id,public_key:pair.jwk,signature:bad},false)).status,401);
    const account=await enroll(f.call,pair);
    assert.equal((await f.call("/api/me/profiles","POST",
      {display_name:"나",items:[],avatar_path:"../../private"},account.token)).status,400);
    assert.equal((await f.call("/api/me/profiles","GET",undefined,
      "appdev_"+"A".repeat(43))).status,401);
  } finally { f.db.close(); }
});


test("S5-07: fixed code links another device, preserves ownership, rotates, and deletes",async()=>{
  const f=fixture();
  try {
    const a=await enroll(f.call,newKeyPair());
    const b=await enroll(f.call,newKeyPair());
    const created=await f.call("/api/me/profiles","POST",
      {display_name:"프로필",items:[{item_type:"SKILL",title:"Unity"}]},a.token);
    assert.equal(created.status,201);
    const id=(await created.json()).data.id;
    assert.equal((await f.call("/api/me/profiles/"+id,"GET",undefined,b.token)).status,404);
    const issued=await f.call("/api/me/profiles/"+id+"/link-code","POST",undefined,a.token);
    assert.equal(issued.status,200);
    const code=(await issued.json()).data.link_code;
    assert.match(code,/^applink_[A-Za-z0-9_-]{32}$/);
    assert.equal((await f.call("/api/me/profiles/"+id+"/link-code","POST",undefined,b.token)).status,404);
    assert.equal((await f.call("/api/me/profile-links","POST",{code},b.token)).status,200);
    assert.equal((await (await f.call("/api/me/profiles","GET",undefined,b.token)).json()).total,1);
    assert.equal((await f.call("/api/me/profiles/"+id,"PUT",
      {display_name:"다른 기기 수정",items:[]},b.token)).status,200);
    assert.equal((await (await f.call("/api/me/profiles/"+id,"GET",undefined,a.token)).json()).data.display_name,"다른 기기 수정");
    const newCode=(await (await f.call("/api/me/profiles/"+id+"/link-code","POST",undefined,b.token)).json()).data.link_code;
    assert.notEqual(code,newCode);
    const c=await enroll(f.call,newKeyPair());
    assert.equal((await f.call("/api/me/profile-links","POST",{code},c.token)).status,404);
    assert.equal((await f.call("/api/me/profile-links","POST",{code:newCode},c.token)).status,200);
    assert.equal(f.db.prepare("SELECT COUNT(*) n FROM profile_devices WHERE profile_id=?").get(id).n,3);
    assert.equal((await f.call("/api/me/profiles/"+id,"DELETE",undefined,a.token)).status,204);
    assert.equal(f.db.prepare("SELECT COUNT(*) n FROM profile_devices").get().n,0);
    assert.equal(f.db.prepare("SELECT COUNT(*) n FROM profile_link_credentials").get().n,0);
  } finally { f.db.close(); }
});

test("S5-07: link-code guessing is bounded; admin-created rows remain unclaimed",async()=>{
  const f=fixture();
  try {
    const a=await enroll(f.call,newKeyPair());
    const legacy=(await (await f.call("/api/profiles","POST",
      {display_name:"관리자 생성",items:[]})).json()).data.id;
    assert.equal((await (await f.call("/api/me/profiles","GET",undefined,a.token)).json()).total,0);
    assert.equal((await f.call("/api/me/profiles/"+legacy+"/link-code","POST",undefined,a.token)).status,404);
    const fake="applink_"+"0".repeat(32);
    for(let i=0;i<5;i++)
      assert.equal((await f.call("/api/me/profile-links","POST",{code:fake},a.token)).status,404);
    assert.equal((await f.call("/api/me/profile-links","POST",{code:fake},a.token)).status,429);
    const code=(await (await f.call("/api/profiles/"+legacy+"/link-code","POST")).json()).data.link_code;
    const b=await enroll(f.call,newKeyPair());
    assert.equal((await f.call("/api/me/profile-links","POST",{code},b.token)).status,200);
    assert.equal((await (await f.call("/api/me/profiles","GET",undefined,b.token)).json()).total,1);
    assert.equal((await f.call("/api/profiles/"+legacy+"/link-code","POST",undefined,b.token)).status,401);
  } finally { f.db.close(); }
});


test("S5-07: losing a linked device can revoke access and invalidate fixed code",async()=>{
  const f=fixture();
  try {
    const a=await enroll(f.call,newKeyPair());
    const b=await enroll(f.call,newKeyPair());
    const own=await f.call("/api/me/profiles","POST",{display_name:"사용자",items:[]},a.token);
    const id=(await own.json()).data.id;
    const issued=await f.call("/api/me/profiles/"+id+"/link-code","POST",undefined,a.token);
    const code=(await issued.json()).data.link_code;
    assert.equal((await f.call("/api/me/profile-links","POST",{code},b.token)).status,200);
    const list=await f.call("/api/me/profiles/"+id+"/devices","GET",undefined,a.token);
    assert.equal(list.status,200);
    assert.equal((await list.json()).data.length,2);
    const revoke=await f.call("/api/me/profiles/"+id+"/devices/"+b.deviceId,"DELETE",undefined,a.token);
    assert.equal(revoke.status,200);
    assert.equal((await revoke.json()).data.link_code_invalidated,true);
    assert.equal((await f.call("/api/me/profiles/"+id,"GET",undefined,b.token)).status,404);
    assert.equal((await f.call("/api/me/profile-links","POST",{code},b.token)).status,404);
    assert.equal((await f.call("/api/me/profiles/"+id+"/devices/"+a.deviceId,"DELETE",undefined,a.token)).status,409,
      "Last owner cannot lose all recovery access.");
    assert.equal((await (await f.call("/api/me/profiles/"+id+"/devices","GET",undefined,a.token)).json()).data.length,1);
  }finally{f.db.close();}
});

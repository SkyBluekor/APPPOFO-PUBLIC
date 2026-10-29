import test from "node:test";
import assert from "node:assert/strict";
import { randomBytes, createHash, randomUUID } from "node:crypto";
import { DatabaseSync } from "node:sqlite";
import { readFileSync } from "node:fs";
import worker from "../src/index.js";
import { encodeFaceBase64Url } from "../src/face-validation.js";

const adminToken = "synthetic-admin-for-s5-09-local-only-123456789";
const deviceToken = "appdev_" + randomBytes(32).toString("base64url");
const secret = randomBytes(32).toString("base64url");
const policy = "synthetic-enroll-policy-v1";
const scanPolicy = "synthetic-scanner-policy-v1";
const a = "11111111-1111-4111-8111-111111111111";
const b = "22222222-2222-4222-8222-222222222222";
const scanner = "33333333-3333-4333-8333-333333333333";
const deviceId = "44444444-4444-4444-8444-444444444444";

function unitAxis(axis) {
  const bytes = new Uint8Array(512);
  new DataView(bytes.buffer).setFloat32(axis * 4,1,true);
  return bytes;
}
function sampleTemplate(startAxis) {
  const bytes = new Uint8Array(1536);
  const view = new DataView(bytes.buffer);
  for(let pose=0;pose<3;pose++)
    view.setFloat32((pose*128+startAxis+pose)*4,1,true);
  return bytes;
}
const body = axis => ({
  request_id:randomUUID(),
  model_version:"sface_2021dec",
  embedding_b64url:encodeFaceBase64Url(unitAxis(axis)),
});
const enrollBody = (axis,discoverable=true,revision=0) => ({
  request_id:randomUUID(),expected_revision:revision,purge_epoch:0,
  model_version:"sface_2021dec",
  embeddings_b64url:encodeFaceBase64Url(sampleTemplate(axis)),
  consent:{policy_version:policy,storage_opt_in:true,discoverable_opt_in:discoverable,
    confirmed_at:Date.now(),subject_present:true},
});

function fixture({matchEnabled=true,localEnabled=true}={}) {
  const db=new DatabaseSync(":memory:");
  db.exec("PRAGMA foreign_keys=ON");
  for(const name of ["0001_profiles.sql","0002_device_ownership.sql",
    "0003_profile_connections.sql","0004_face_enrollment.sql",
    "0005_face_scan_grants.sql","0007_profile_avatars.sql"])
    db.exec(readFileSync(new URL("../migrations/"+name,import.meta.url),"utf8"));
  const blob=x => Array.isArray(x)?Uint8Array.from(x):x;
  const prepare=(sql,params=[])=>({
    bind:(...p)=>prepare(sql,p),
    first:async()=>db.prepare(sql).get(...params.map(blob))??null,
    all:async()=>({results:db.prepare(sql).all(...params.map(blob))}),
    run:async()=>({meta:{changes:Number(db.prepare(sql).run(...params.map(blob)).changes)}}),
  });
  const DB={prepare:sql=>prepare(sql),async batch(cmds){
    db.exec("BEGIN");
    try { const out=[];for(const cmd of cmds)out.push(await cmd.run());db.exec("COMMIT");return out; }
    catch(e){db.exec("ROLLBACK");throw e;}
  }};
  const env={DB, APPPOFO_API_TOKEN:adminToken,
    APPPOFO_FACE_POLICY_VERSION:policy,APPPOFO_FACE_KEY_V1:secret,
    APPPOFO_FACE_SCAN_POLICY_VERSION:scanPolicy,
    APPPOFO_FACE_MATCH_THRESHOLD_TEST:"0.75",
    APPPOFO_FACE_MATCH_MARGIN_TEST:"0.05",
    ...(localEnabled?{APPPOFO_FACE_LOCAL_TEST_MODE:"enabled"}:{}),
    ...(matchEnabled?{APPPOFO_FACE_MATCH_LOCAL_TEST_MODE:"enabled"}:{})};
  for(const [id,name,head] of [
    [scanner,"스캐너 사용자",""],
    [a,"가상 A","A 개발자"],
    [b,"가상 B","B 기획자"],
  ]) db.prepare("INSERT INTO profiles(id,display_name,headline,email,created_at,updated_at) " +
      "VALUES(?,?,?,?,?,?)").run(id,name,head,"private@invalid.test",1,1);
  db.prepare("INSERT INTO device_identities(id,public_key_jwk,key_thumbprint,created_at)" +
    " VALUES(?,?,?,?)").run(deviceId,"{}","synthetic-thumb",1);
  db.prepare("INSERT INTO profile_devices(profile_id,device_id,granted_at)" +
    " VALUES(?,?,?)").run(scanner,deviceId,1);
  db.prepare("INSERT INTO device_sessions(token_hash,device_id,expires_at)" +
    " VALUES(?,?,?)").run(createHash("sha256").update(deviceToken).digest("base64url"),
      deviceId,Date.now()+300000);
  const call=(path,method="GET",data,authorization=deviceToken)=>{
    const headers={...(authorization?{Authorization:"Bearer "+authorization}:{}),
      ...(data===undefined?{}:{"Content-Type":"application/json"})};
    return worker.fetch(new Request("https://local.test"+path,{
      method,headers,...(data===undefined?{}:{body:JSON.stringify(data)}),
    }),env);
  };
  return {db,env,call,close:()=>db.close(),async enroll(id,axis,disc=true,rev=0){
    const r=await call("/api/profiles/"+id+"/face","POST",
      enrollBody(axis,disc,rev),adminToken);
    assert.ok([200,201].includes(r.status),"enrollment HTTP "+r.status);
  },consent:()=>call("/api/me/face/scan-consent","POST",
    {consent:true,policy_version:scanPolicy}),
  match:async(axis)=>call("/api/me/face/match","POST",body(axis))};
}

test("local-only encrypted 1:N match returns only public name/headline and UUID",async()=>{
  const f=fixture();
  try{
    await f.enroll(a,0);
    await f.enroll(b,10);
    assert.equal((await f.match(0)).status,403,"scan without opt-in must fail");
    assert.equal((await f.consent()).status,204);
    const matched=await f.match(0);
    assert.equal(matched.status,200);
    assert.deepEqual(await matched.json(),{
      matched:true,profile:{id:a,display_name:"가상 A",headline:"A 개발자",business_card_theme:"AURORA",avatar_base64:null},
      decision_version:"local-synthetic-v1",
    });
    const other=await f.match(10);
    assert.equal((await other.json()).profile.id,b);
    assert.deepEqual(await (await f.match(80)).json(),{matched:false});
  }finally{f.close();}
});

test("private consent, ambiguity, inactive and deleted profiles cannot match",async()=>{
  const f=fixture();
  try{
    await f.enroll(a,0,false);
    await f.enroll(b,20);
    await f.consent();
    assert.deepEqual(await(await f.match(0)).json(),{matched:false});
    await f.enroll(a,20,true,1); // identical to B: ambiguous and fail closed.
    assert.deepEqual(await(await f.match(20)).json(),{matched:false});
    f.db.prepare("UPDATE profiles SET is_active=0 WHERE id=?").run(b);
    assert.equal((await(await f.match(20)).json()).profile.id,a);
    const del=await f.call("/api/profiles/"+a+"/face","DELETE",{
      request_id:randomUUID(),expected_revision:2,purge_epoch:0,
    },adminToken);
    assert.equal(del.status,204);
    assert.deepEqual(await(await f.match(20)).json(),{matched:false});
  }finally{f.close();}
});

test("revoked scan consent, revoked device link and expired session block search",async()=>{
  const f=fixture();
  try {
    await f.enroll(a,0);
    await f.consent();
    assert.equal((await f.call("/api/me/face/scan-consent","DELETE")).status,204);
    assert.equal((await f.match(0)).status,403);
    await f.consent();
    f.db.prepare("UPDATE profile_devices SET revoked_at=? WHERE profile_id=? AND device_id=?")
      .run(Date.now(),scanner,deviceId);
    assert.equal((await f.match(0)).status,403);
    f.db.prepare("UPDATE profile_devices SET revoked_at=NULL WHERE profile_id=? AND device_id=?")
      .run(scanner,deviceId);
    f.db.prepare("UPDATE device_sessions SET expires_at=0").run();
    assert.equal((await f.match(0)).status,401);
    assert.equal((await f.call("/api/me/face/match","POST",body(0),adminToken)).status,401);
  }finally {f.close();}
});

test("dedicated matching gate defaults closed even when enrollment is enabled",async()=>{
  const f=fixture({matchEnabled:false});
  try{
    assert.equal((await f.consent()).status,503);
    assert.equal((await f.match(0)).status,503);
    f.env.APPPOFO_FACE_MATCH_LOCAL_TEST_MODE="enabled";
    f.env.APPPOFO_FACE_MATCH_THRESHOLD_TEST=undefined;
    await f.consent();
    assert.equal((await f.match(0)).status,503);
    f.env.APPPOFO_FACE_MATCH_THRESHOLD_TEST="0.75";
    f.env.APPPOFO_FACE_LOCAL_TEST_MODE=undefined;
    assert.equal((await f.match(0)).status,503);
  }finally{f.close();}
});

test("malformed or wrong-model vectors fail, valid but ambiguous scores do not identify",async()=>{
  const f=fixture();
  try{
    await f.enroll(a,0);
    await f.enroll(b,0);
    await f.consent();
    assert.deepEqual(await (await f.match(0)).json(),{matched:false});
    const route="/api/me/face/match";
    assert.equal((await f.call(route,"POST",{...body(0),model_version:"unknown"})).status,422);
    assert.equal((await f.call(route,"POST",{...body(0),embedding_b64url:"QQ"})).status,400);
    assert.equal((await f.call(route,"POST",{
      ...body(0),embedding_b64url:encodeFaceBase64Url(new Uint8Array(512))})).status,400);
    assert.equal((await f.call(route,"POST",{...body(0),extra:"secret"})).status,400);
  }finally{f.close();}
});

test("rate limit is enforced across repeated consent and candidate overflow fails closed",async()=>{
  const f=fixture();
  try{
    await f.enroll(a,0);
    await f.consent();
    for(let i=0;i<118;i++) assert.equal((await f.match(0)).status,200);
    await f.consent(); // re-consent must NOT reset rate counter.
    assert.equal((await f.match(0)).status,200);
    assert.equal((await f.match(0)).status,200);
    assert.equal((await f.match(0)).status,429);
  }finally{f.close();}
  const g=fixture();
  try{
    await g.enroll(a,0);
    await g.consent();
    const tmpl=g.db.prepare("SELECT * FROM face_templates WHERE profile_id=?").get(a);
    for(let i=0;i<25;i++){
      const id="55555555-5555-4555-8555-"+String(i).padStart(12,"0");
      g.db.prepare("INSERT INTO profiles(id,display_name,created_at,updated_at) VALUES(?,?,?,?)")
        .run(id,"synthetic",1,1);
      g.db.prepare("INSERT INTO face_consent_state(profile_id,revision,purge_epoch,"+
        "storage_consent,discoverable) VALUES(?,1,0,1,1)").run(id);
      g.db.prepare("INSERT INTO face_templates(profile_id,model_version,cipher_suite,"+
        "key_version,nonce,ciphertext,revision,enrolled_at,updated_at) "+
        "VALUES(?,?,?,?,?,?,?,?,?)")
        .run(id,tmpl.model_version,tmpl.cipher_suite,tmpl.key_version,tmpl.nonce,
          tmpl.ciphertext,1,1,1);
    }
    assert.equal((await g.match(0)).status,503,
      "26 eligible candidates must fail before partial matching");
  }finally{g.close();}
});

test("global purge-epoch change prevents stale matching even if templates linger",async()=>{
  const f=fixture();
  try{
    await f.enroll(a,0);
    await f.consent();
    assert.equal((await(await f.match(0)).json()).matched,true);
    f.db.prepare("UPDATE face_system_state SET purge_epoch=purge_epoch+1").run();
    assert.deepEqual(await(await f.match(0)).json(),{matched:false});
  }finally{f.close();}
});

test('public recognition exposes its summary without extra opt-in; scanner consent and private field isolation remain',async()=>{
  const f=fixture();
  try {
    await f.enroll(a,0);
    const route='/api/me/face/profiles/'+a+'/summary';
    assert.equal((await f.call(route)).status,403);
    await f.consent();
    f.db.prepare('UPDATE profiles SET bio=?,website=? WHERE id=?').run('A public introduction','https://private.invalid',a);
    f.db.prepare('INSERT INTO profile_items(profile_id,item_type,title,description,url) VALUES(?,?,?,?,?)').run(a,'PROJECT','AppPofo','Project intro','https://example.org/project');
    f.db.prepare('INSERT INTO profile_items(profile_id,item_type,title,url) VALUES(?,?,?,?)').run(a,'SKILL','Unity','');
    f.db.prepare('INSERT INTO profile_items(profile_id,item_type,title,url) VALUES(?,?,?,?)').run(a,'LINK','GitHub','https://github.com/example');
    f.db.prepare('INSERT INTO profile_items(profile_id,item_type,title,url) VALUES(?,?,?,?)').run(a,'LINK','Unsafe','javascript:alert(1)');
    const data=(await(await f.call(route)).json()).data;
    assert.equal(data.bio,'A public introduction');
    assert.deepEqual(data.skills,['Unity']);assert.equal(data.project.title,'AppPofo');assert.equal(data.links.length,1);
    assert.deepEqual(Object.keys(data).sort(),['id','display_name','headline','avatar_base64','bio','skills','project','links'].sort());
    const text=JSON.stringify(data); for(const secret of ['private@invalid.test','private.invalid','embedding','nonce','ciphertext','javascript:']) assert.ok(!text.includes(secret));
    await f.call('/api/me/face/scan-consent','DELETE');
    assert.equal((await f.call(route)).status,403);
  } finally {f.close();}
});

test('summary rechecks public face state, active profile, current revision and purge epoch',async()=>{
  const f=fixture();try {
    await f.enroll(a,0);await f.consent();const route='/api/me/face/profiles/'+a+'/summary';
    assert.equal((await f.call(route)).status,200);
    f.db.prepare('UPDATE face_consent_state SET discoverable=0 WHERE profile_id=?').run(a);assert.equal((await f.call(route)).status,404);
    f.db.prepare('UPDATE face_consent_state SET discoverable=1 WHERE profile_id=?').run(a);
    f.db.prepare('UPDATE profiles SET is_active=0 WHERE id=?').run(a);assert.equal((await f.call(route)).status,404);
    f.db.prepare('UPDATE profiles SET is_active=1 WHERE id=?').run(a);
    f.db.prepare('UPDATE face_consent_state SET revision=revision+1 WHERE profile_id=?').run(a);assert.equal((await f.call(route)).status,404);
    f.db.prepare('UPDATE face_consent_state SET revision=revision-1 WHERE profile_id=?').run(a);
    f.db.prepare('UPDATE face_system_state SET purge_epoch=purge_epoch+1').run();assert.equal((await f.call(route)).status,404);
    assert.equal((await f.call(route,'POST',{})).status,405);
  }finally{f.close();}
});

test('summary bounds long content and item counts and keeps its GET read-only',async()=>{
  const f=fixture();try {
    await f.enroll(a,0);await f.consent();
    f.db.prepare('UPDATE profiles SET bio=? WHERE id=?').run('가'.repeat(500),a);
    for(let i=0;i<8;i++) f.db.prepare('INSERT INTO profile_items(profile_id,item_type,title,url,sort_order) VALUES(?,?,?,?,?)').run(a,'SKILL','Skill '+i,'',i);
    for(let i=0;i<4;i++) f.db.prepare('INSERT INTO profile_items(profile_id,item_type,title,description,url,sort_order) VALUES(?,?,?,?,?,?)').run(a,'PROJECT','Project '+i,'D'.repeat(500),'https://example.org/'+i,i);
    for(let i=0;i<4;i++) f.db.prepare('INSERT INTO profile_items(profile_id,item_type,title,url,sort_order) VALUES(?,?,?,?,?)').run(a,'LINK','Link '+i,'https://example.org/'+i,i);
    const before=f.db.prepare('SELECT * FROM face_scan_grants').all();
    const result=(await(await f.call('/api/me/face/profiles/'+a+'/summary')).json()).data;
    assert.equal(result.bio.length,140);assert.equal(result.skills.length,5);assert.equal(result.links.length,2);
    assert.equal(result.project.title,'Project 0');assert.equal(result.project.description.length,140);
    assert.deepEqual(f.db.prepare('SELECT * FROM face_scan_grants').all(),before);
  }finally{f.close();}
});

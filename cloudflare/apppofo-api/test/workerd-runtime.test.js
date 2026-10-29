import test from "node:test";
import { generateKeyPairSync, sign, createHash, randomBytes } from "node:crypto";
import { encodeFaceBase64Url } from "../src/face-validation.js";
import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { convertV4MiniflareOptions, Miniflare } from "miniflare";
import { runStagingSmoke } from "../tools/staging_smoke.mjs";

const sourceRoot = new URL("../src/", import.meta.url);
const moduleNames = ["index.js", ...readdirSync(sourceRoot)
  .filter((name) => name.endsWith(".js") && name !== "index.js")];
const schema = ["0001_profiles.sql","0002_device_ownership.sql","0003_profile_connections.sql","0004_face_enrollment.sql","0005_face_scan_grants.sql","0006_staging_integration.sql","0007_profile_avatars.sql"]
  .map(file => readFileSync(new URL("../migrations/"+file, import.meta.url),"utf8")).join("\n");
const TOKEN = "only-for-workerd-integration-testing-1234567890";
const origin = "https://apppofo.test";
const faceHeaders = {"If-Face-Consent-Generation":"0"};

async function signedDevice(request) {
  const pair=generateKeyPairSync("ec",{namedCurve:"prime256v1"});
  const pub=pair.publicKey.export({format:"jwk"});
  const jwk={kty:"EC",crv:"P-256",x:pub.x,y:pub.y};
  const challenge=await request("/api/device/challenge","POST",{purpose:"register",public_key:jwk},"");
  assert.equal(challenge.status,201);
  const challengeId=(await challenge.json()).challenge_id;
  const thumb=createHash("sha256").update("AppPofo-P256-v1\n"+jwk.x+"\n"+jwk.y).digest("base64url");
  const signature=sign("sha256",Buffer.from("AppPofo-v1\nregister\n"+challengeId+"\n"+thumb),pair.privateKey).toString("base64url");
  const registered=await request("/api/device/register","POST",{challenge_id:challengeId,public_key:jwk,signature},"");
  assert.equal(registered.status,201);
  const id=(await registered.json()).device_id;
  async function renew() {
    const started=await request("/api/device/challenge","POST",{purpose:"session",device_id:id},"");
    assert.equal(started.status,201);
    const cid=(await started.json()).challenge_id;
    const sig=sign("sha256",Buffer.from("AppPofo-v1\nsession\n"+cid+"\n"+id),pair.privateKey).toString("base64url");
    const session=await request("/api/device/session","POST",{challenge_id:cid,device_id:id,signature:sig},"");
    assert.equal(session.status,200);
    return (await session.json()).access_token;
  }
  async function recoverRegistration() {
    const challenge=await request("/api/device/challenge","POST",{purpose:"register",public_key:jwk},"");
    assert.equal(challenge.status,201);
    const cid=(await challenge.json()).challenge_id;
    const sig=sign("sha256",Buffer.from("AppPofo-v1\nregister\n"+cid+"\n"+thumb),pair.privateKey).toString("base64url");
    return request("/api/device/register","POST",{challenge_id:cid,public_key:jwk,signature:sig},"");
  }
  return {id,token:await renew(),renew,recoverRegistration};
}

function syntheticEnrollment(revision=0,discoverable=true) {
  const data=new Uint8Array(1536),view=new DataView(data.buffer);
  for(let p=0;p<3;p++)view.setFloat32((p*128+p)*4,1,true);
  return {request_id:crypto.randomUUID(),expected_revision:revision,purge_epoch:0,
    model_version:"sface_2021dec",embeddings_b64url:encodeFaceBase64Url(data),
    consent:{policy_version:"synthetic-face-test-v1",storage_opt_in:true,discoverable_opt_in:discoverable,
      confirmed_at:Date.now(),subject_present:true}};
}
function syntheticQuery() {
  const data=new Uint8Array(512);new DataView(data.buffer).setFloat32(0,1,true);
  return {request_id:crypto.randomUUID(),model_version:"sface_2021dec",embedding_b64url:encodeFaceBase64Url(data)};
}

test("profile avatar owner isolation, default preservation, public match and delete cascade",async t=>{
  const {db,request}=await fixture(t,{stage:true});
  const owner=await signedDevice(request), other=await signedDevice(request);
  const created=await request('/api/me/profiles','POST',{display_name:'Synthetic avatar owner',items:[]},owner.token,{'Idempotency-Key':crypto.randomUUID()});
  const id=(await created.json()).data.id;
  const image=readFileSync(new URL('./fixtures/avatar.jpg',import.meta.url)).toString('base64');
  const path='/api/me/profiles/'+id+'/avatar';
  const payload={image_base64:image,only_if_missing:true};
  assert.equal((await request(path,'PUT',payload,'')).status,401);
  assert.equal((await request(path,'PUT',payload,other.token)).status,404);
  assert.equal((await request(path,'PUT',{...payload,image_base64:'not an image'},owner.token)).status,400);
  const saved=await request(path,'PUT',payload,owner.token);assert.equal(saved.status,200);assert.equal((await saved.json()).data.saved,true);
  const again=await request(path,'PUT',payload,owner.token);assert.equal((await again.json()).data.saved,false);
  assert.equal((await (await request('/api/me/profiles/'+id,'GET',undefined,owner.token)).json()).data.avatar_base64,image);
  assert.equal((await (await request('/api/me/profiles','GET',undefined,owner.token)).json()).data[0].avatar_base64,image);
  assert.equal((await (await request('/api/profiles','GET')).json()).data[0].avatar_base64,image);
  const enroll=syntheticEnrollment();
  assert.equal((await request('/api/me/profiles/'+id+'/face','POST',enroll,owner.token,faceHeaders)).status,201);
  assert.equal((await request('/api/me/face/scan-consent','POST',{consent:true,policy_version:'synthetic-scanner-policy-v1'},owner.token)).status,204);
  const match=await request('/api/me/face/match','POST',syntheticQuery(),owner.token);
  assert.equal((await match.json()).profile.avatar_base64,image);
  assert.equal((await request('/api/me/profiles/'+id,'DELETE',undefined,other.token)).status,404);
  assert.equal((await request('/api/me/profiles/'+id,'DELETE',undefined,owner.token)).status,204);
  assert.equal((await db.prepare('SELECT COUNT(*) n FROM profile_avatars').first()).n,0);
  assert.equal((await db.prepare('SELECT COUNT(*) n FROM face_templates').first()).n,0);
  assert.equal((await db.prepare('SELECT COUNT(*) n FROM profile_devices').first()).n,0);
});

test("saved scan consent survives signed sessions, reads never opt in, changed policy and revocation require approval",async t=>{
  const {db,request}=await fixture(t,{stage:true});
  const owner=await signedDevice(request);
  await request('/api/me/profiles','POST',{display_name:'Synthetic consent owner',items:[]},owner.token,{'Idempotency-Key':crypto.randomUUID()});
  const path='/api/me/face/scan-consent';
  assert.equal((await request(path,'GET',undefined,'')).status,401);
  assert.equal((await (await request(path,'GET',undefined,owner.token)).json()).approved,false);
  assert.equal((await db.prepare('SELECT COUNT(*) n FROM face_scan_grants').first()).n,0);
  await request(path,'POST',{consent:true,policy_version:'synthetic-scanner-policy-v1'},owner.token);
  await db.prepare('UPDATE face_scan_grants SET request_count=17 WHERE device_id=?').bind(owner.id).run();
  owner.token=await owner.renew();
  for(let i=0;i<3;i++) assert.equal((await (await request(path,'GET',undefined,owner.token)).json()).approved,true);
  assert.equal((await db.prepare('SELECT request_count n FROM face_scan_grants WHERE device_id=?').bind(owner.id).first()).n,17);
  await db.prepare('UPDATE face_scan_grants SET policy_version=? WHERE device_id=?').bind('older-policy',owner.id).run();
  assert.equal((await (await request(path,'GET',undefined,owner.token)).json()).approved,false);
  await request(path,'POST',{consent:true,policy_version:'synthetic-scanner-policy-v1'},owner.token);
  await request(path,'DELETE',undefined,owner.token);
  assert.equal((await (await request(path,'GET',undefined,owner.token)).json()).approved,false);
});

test("S5-10 prepared remote smoke executes synthetic-only lifecycle against isolated staging Worker",async t=>{
  const {db,request}=await fixture(t,{stage:true});
  const result=await runStagingSmoke({baseUrl:"https://apppofo-profile-api-staging.synthetic.workers.dev",confirmed:true,
    fetchFn:async(url,options)=>request(new URL(url).pathname,options.method,options.body?JSON.parse(options.body):undefined,
      options.headers.Authorization?.slice(7)??"",options.headers)});
  assert.deepEqual(result,{passed:true,cleaned:true});
  assert.equal((await db.prepare("SELECT COUNT(*) n FROM profiles").first()).n,0);
  assert.equal((await db.prepare("SELECT COUNT(*) n FROM face_templates").first()).n,0);
});

test("S5-10 staging: two signed devices, idempotent profile, encrypted enrollment, public match and withdrawal",async t=>{
  const {db,request}=await fixture(t,{stage:true});
  const owner=await signedDevice(request), scanner=await signedDevice(request);
  const recovered=await owner.recoverRegistration();
  assert.equal(recovered.status,201);assert.equal((await recovered.json()).device_id,owner.id);
  assert.equal((await db.prepare("SELECT COUNT(*) n FROM device_identities").first()).n,2);
  const config=await request("/api/me/face/config","GET",undefined,owner.token);
  assert.equal(config.status,200);
  assert.equal((await config.json()).decision_version,"staging-unvalidated-v1");
  const draft={display_name:"Synthetic participant",headline:"Synthetic role",bio:"Synthetic bio",
    email:"private@example.invalid",items:[{item_type:"SKILL",title:"Skill"},{item_type:"PROJECT",title:"Project"},{item_type:"LINK",title:"Link"}]};
  const key=crypto.randomUUID(),headers={"Idempotency-Key":key};
  const create=await request("/api/me/profiles","POST",draft,owner.token,headers);
  assert.equal(create.status,201);const id=(await create.json()).data.id;
  const replay=await request("/api/me/profiles","POST",draft,owner.token,headers);
  assert.equal(replay.status,201);assert.equal((await replay.json()).data.id,id);
  assert.equal((await db.prepare("SELECT COUNT(*) AS n FROM profiles").first()).n,1);
  assert.equal((await request("/api/me/profiles","POST",{...draft,headline:"changed"},owner.token,headers)).status,409);
  assert.equal((await request("/api/me/profiles/"+id,"GET",undefined,scanner.token)).status,404);
  const path="/api/me/profiles/"+id+"/face";
  assert.equal((await request(path,"GET",undefined,scanner.token)).status,404);
  const enrollment=syntheticEnrollment();
  assert.equal((await request(path,"POST",enrollment,owner.token,faceHeaders)).status,201);
  let stored=await db.prepare("SELECT revision,ciphertext,nonce FROM face_templates WHERE profile_id=?").bind(id).first();
  assert.equal(stored.revision,1);assert.equal(stored.ciphertext.length,1552);
  const initialCipher=Buffer.from(stored.ciphertext);
  assert.equal(initialCipher.includes(Buffer.from(enrollment.embeddings_b64url)),false);
  assert.equal((await request(path,"POST",{...syntheticEnrollment(1),model_version:"wrong"},owner.token,faceHeaders)).status,422);
  stored=await db.prepare("SELECT revision,ciphertext FROM face_templates WHERE profile_id=?").bind(id).first();
  assert.equal(stored.revision,1);assert.deepEqual(Buffer.from(stored.ciphertext),initialCipher);
  const replacement=syntheticEnrollment(1);
  assert.equal((await request(path,"POST",replacement,owner.token,faceHeaders)).status,200);
  assert.equal((await request(path,"POST",replacement,owner.token,faceHeaders)).status,200);
  assert.equal((await db.prepare("SELECT revision FROM face_templates WHERE profile_id=?").bind(id).first()).revision,2);
  assert.equal((await request("/api/me/profiles/"+id,"PUT",{...draft,bio:"updated"},owner.token)).status,200);
  assert.equal((await db.prepare("SELECT revision FROM face_templates WHERE profile_id=?").bind(id).first()).revision,2);
  const detail=await request("/api/me/profiles/"+id,"GET",undefined,owner.token);
  assert.equal((await detail.json()).data.items.length,3);
  assert.equal((await request("/api/me/profiles","POST",{display_name:"Synthetic scanner",items:[]},scanner.token)).status,201);
  const query=syntheticQuery(),url="/api/me/face/match";
  assert.equal((await request(url,"POST",query,scanner.token)).status,403);
  assert.equal((await request("/api/me/face/scan-consent","POST",{consent:true,policy_version:"synthetic-scanner-policy-v1"},scanner.token)).status,204);
  const matched=await request(url,"POST",query,scanner.token);assert.equal(matched.status,200);
  assert.deepEqual(await matched.json(),{matched:true,profile:{id,display_name:draft.display_name,headline:draft.headline,business_card_theme:"AURORA",avatar_base64:null},decision_version:"staging-unvalidated-v1"});
  assert.equal((await request(url,"POST",query,TOKEN)).status,401);
  assert.equal((await request(url,"POST",{...query,model_version:"wrong"},scanner.token)).status,422);
  stored=await db.prepare("SELECT ciphertext FROM face_templates WHERE profile_id=?").bind(id).first();
  const beforeWithdraw=Buffer.from(stored.ciphertext);
  assert.equal((await request(path,"PATCH",{discoverable:false,expected_revision:1,purge_epoch:0},owner.token)).status,409);
  assert.equal((await request(path,"PATCH",{discoverable:false,expected_revision:2,purge_epoch:0},scanner.token)).status,404);
  const withdrawn=await request(path,"PATCH",{discoverable:false,expected_revision:2,purge_epoch:0},owner.token);
  assert.equal(withdrawn.status,200);assert.equal((await withdrawn.json()).data.discoverable,false);
  const afterWithdraw=await db.prepare("SELECT revision,ciphertext FROM face_templates WHERE profile_id=?").bind(id).first();
  assert.equal(afterWithdraw.revision,2);assert.deepEqual(Buffer.from(afterWithdraw.ciphertext),beforeWithdraw);
  assert.deepEqual(await (await request(url,"POST",query,scanner.token)).json(),{matched:false});
  assert.equal((await request(path,"DELETE",{request_id:crypto.randomUUID(),expected_revision:2,purge_epoch:0},owner.token)).status,204);
  assert.equal((await db.prepare("SELECT COUNT(*) n FROM face_templates WHERE profile_id=?").bind(id).first()).n,0);
  assert.equal((await request("/api/me/profiles/"+id,"GET",undefined,owner.token)).status,200);
  assert.equal((await request("/api/me/face/scan-consent","DELETE",undefined,scanner.token)).status,204);
  assert.equal((await request(url,"POST",query,scanner.token)).status,403);
  await db.prepare("UPDATE device_sessions SET expires_at=0 WHERE device_id=?").bind(scanner.id).run();
  assert.equal((await request(url,"POST",query,scanner.token)).status,401);
  assert.ok((await scanner.renew()).startsWith("appdev_"));
  await request("/api/me/profiles/"+id,"DELETE",undefined,owner.token);
  assert.equal((await request("/api/me/profiles","POST",draft,owner.token,headers)).status,409,"Deleted creation keys must never recreate a profile");
});

test("R4-PERF-04 scanner withdraws its duplicate public face while retaining enrollment and search permission",async t=>{
  const {db,request}=await fixture(t,{stage:true});
  const target=await signedDevice(request), scanner=await signedDevice(request);
  async function enroll(device,name) {
    const created=await request("/api/me/profiles","POST",{display_name:name,items:[]},device.token);
    assert.equal(created.status,201);
    const id=(await created.json()).data.id,path="/api/me/profiles/"+id+"/face";
    assert.equal((await request(path,"POST",syntheticEnrollment(),device.token,faceHeaders)).status,201);
    return {id,path};
  }
  const remaining=await enroll(target,"Synthetic remaining target");
  const own=await enroll(scanner,"Synthetic duplicate scanner");
  assert.equal((await request("/api/me/face/scan-consent","POST",
    {consent:true,policy_version:"synthetic-scanner-policy-v1"},scanner.token)).status,204);
  const eligible=()=>db.prepare("SELECT COUNT(*) n FROM face_templates t JOIN profiles p ON p.id=t.profile_id JOIN face_consent_state c ON c.profile_id=t.profile_id WHERE p.is_active=1 AND c.storage_consent=1 AND c.discoverable=1 AND c.revision=t.revision AND c.purge_epoch=(SELECT purge_epoch FROM face_system_state WHERE singleton=1) AND t.model_version='sface_2021dec'").first();
  const snapshot=async()=>({
    template:await db.prepare("SELECT * FROM face_templates WHERE profile_id=?").bind(own.id).first(),
    consent:await db.prepare("SELECT revision,purge_epoch,storage_consent,discoverable,consent_generation FROM face_consent_state WHERE profile_id=?").bind(own.id).first(),
    links:(await db.prepare("SELECT * FROM profile_devices WHERE device_id=?").bind(scanner.id).all()).results,
    grant:await db.prepare("SELECT * FROM face_scan_grants WHERE device_id=?").bind(scanner.id).first(),
    sessions:(await db.prepare("SELECT * FROM device_sessions WHERE device_id=?").bind(scanner.id).all()).results,
  });
  assert.equal((await eligible()).n,2);
  let response=await request("/api/me/face/match","POST",syntheticQuery(),scanner.token);
  assert.equal(response.status,200);assert.deepEqual(await response.json(),{matched:false});
  const before=await snapshot();
  const withdrawn=await request(own.path,"PATCH",{discoverable:false,expected_revision:1,purge_epoch:0},scanner.token);
  assert.equal(withdrawn.status,200);
  const after=await snapshot();
  assert.deepEqual(after.template,before.template,"Encrypted template including nonce and revision must be unchanged");
  assert.deepEqual(after.links,before.links);assert.deepEqual(after.grant,before.grant);
  assert.deepEqual(after.sessions,before.sessions);
  assert.deepEqual(after.consent,{...before.consent,discoverable:0,consent_generation:before.consent.consent_generation+1});
  const status=(await (await request(own.path,"GET",undefined,scanner.token)).json()).data;
  assert.equal(status.registered,true);assert.equal(status.storage_consent,true);
  assert.equal(status.revision,1);assert.equal(status.discoverable,false);
  assert.equal((await eligible()).n,1);
  response=await request("/api/me/face/match","POST",syntheticQuery(),scanner.token);
  assert.equal(response.status,200);assert.equal((await response.json()).profile.id,remaining.id);
  assert.equal((await request(own.path,"PATCH",{discoverable:true,expected_revision:1,purge_epoch:0},scanner.token)).status,400,
    "Existing PATCH cannot republish the retained template");
  assert.deepEqual((await snapshot()).template,before.template);
  assert.equal((await eligible()).n,1);
});

for (const threshold of ["0.75","0.65","0.64"])
test("staging threshold comparison serves a synthetic 0.70 face score at threshold "+threshold,async t=>{
  const {request}=await fixture(t,{stage:true,overrides:{APPPOFO_FACE_MATCH_THRESHOLD:threshold}});
  const owner=await signedDevice(request),scanner=await signedDevice(request);
  const config=await request("/api/me/face/config","GET",undefined,scanner.token);
  assert.equal(config.status,threshold==="0.64"?503:200);
  const created=await request("/api/me/profiles","POST",{display_name:"Synthetic threshold target",items:[]},owner.token);
  assert.equal(created.status,201);const id=(await created.json()).data.id;
  assert.equal((await request("/api/me/profiles/"+id+"/face","POST",syntheticEnrollment(),owner.token,faceHeaders)).status,201);
  assert.equal((await request("/api/me/profiles","POST",{display_name:"Synthetic threshold scanner",items:[]},scanner.token)).status,201);
  assert.equal((await request("/api/me/face/scan-consent","POST",
    {consent:true,policy_version:"synthetic-scanner-policy-v1"},scanner.token)).status,204);
  const bytes=new Uint8Array(512),view=new DataView(bytes.buffer);
  view.setFloat32(0,0.70,true);view.setFloat32(3*4,Math.sqrt(1-0.70**2),true);
  const response=await request("/api/me/face/match","POST",{
    request_id:crypto.randomUUID(),model_version:"sface_2021dec",embedding_b64url:encodeFaceBase64Url(bytes)},scanner.token);
  assert.equal(response.status,threshold==="0.64"?503:200);
  const result=await response.json();
  if (threshold==="0.65") { assert.equal(result.matched,true);assert.equal(result.profile.id,id); }
  else if (threshold==="0.75") assert.deepEqual(result,{matched:false});
  else assert.equal(result.error.code,"FACE_MATCH_POLICY_NOT_CONFIGURED");
});

test("S5-10 staging admin approves a signed scanner; consent, rate and revocation remain independent",async t=>{
  const {request,db}=await fixture(t,{stage:true});
  const scanner=await signedDevice(request);
  const consent={consent:true,policy_version:"synthetic-scanner-policy-v1"};
  const auth={device_id:scanner.id};
  assert.equal((await request("/api/admin/face-scanners","POST",auth,scanner.token)).status,401);
  assert.equal((await request("/api/me/face/scan-consent","POST",consent,scanner.token)).status,403);
  assert.equal((await request("/api/admin/face-scanners","POST",auth)).status,204);
  assert.equal((await request("/api/me/face/scan-consent","POST",consent,scanner.token)).status,204);
  for(let i=0;i<120;i++)assert.equal((await request("/api/me/face/match","POST",syntheticQuery(),scanner.token)).status,200);
  assert.equal((await request("/api/me/face/match","POST",syntheticQuery(),scanner.token)).status,429);
  await request("/api/me/face/scan-consent","DELETE",undefined,scanner.token);
  await request("/api/me/face/scan-consent","POST",consent,scanner.token);
  assert.equal((await request("/api/me/face/match","POST",syntheticQuery(),scanner.token)).status,429);
  assert.equal((await request("/api/admin/face-scanners","DELETE",auth)).status,204);
  assert.equal((await request("/api/me/face/match","POST",syntheticQuery(),scanner.token)).status,403);
  assert.equal((await db.prepare("SELECT COUNT(*) n FROM face_templates").first()).n,0);
});

test("S5-10 enrollment mutation quota preserves the old template and never blocks privacy withdrawal",async t=>{
  const {request,db}=await fixture(t,{stage:true});
  const owner=await signedDevice(request);
  const created=await request("/api/me/profiles","POST",{display_name:"Synthetic quota",items:[]},owner.token);
  const id=(await created.json()).data.id,path="/api/me/profiles/"+id+"/face",body=syntheticEnrollment();
  assert.equal((await request(path,"POST",body,owner.token,faceHeaders)).status,201);
  for(let i=0;i<11;i++)assert.equal((await request(path,"POST",body,owner.token,faceHeaders)).status,200);
  assert.equal((await request(path,"POST",syntheticEnrollment(1),owner.token,faceHeaders)).status,429);
  assert.equal((await db.prepare("SELECT revision FROM face_templates WHERE profile_id=?").bind(id).first()).revision,1);
  assert.equal((await request(path,"PATCH",{discoverable:false,expected_revision:1,purge_epoch:0},owner.token)).status,200);
  assert.equal((await request(path,"DELETE",{request_id:crypto.randomUUID(),expected_revision:1,purge_epoch:0},owner.token)).status,204);
  assert.equal((await request("/api/me/face/scan-consent","DELETE",undefined,owner.token)).status,204);
});

test("S5-10 public withdrawal invalidates an older pending enrollment without changing encrypted AAD revision",async t=>{
  const {request,db}=await fixture(t,{stage:true});
  const owner=await signedDevice(request);
  const created=await request("/api/me/profiles","POST",{display_name:"Synthetic consent race",items:[]},owner.token);
  const id=(await created.json()).data.id,path="/api/me/profiles/"+id+"/face";
  assert.equal((await request(path,"POST",syntheticEnrollment(),owner.token)).status,400,"Staging requires the approval snapshot header");
  assert.equal((await request(path,"POST",syntheticEnrollment(),owner.token,faceHeaders)).status,201);
  const pending=syntheticEnrollment(1),before=Buffer.from((await db.prepare("SELECT ciphertext FROM face_templates WHERE profile_id=?").bind(id).first()).ciphertext);
  assert.equal((await request(path,"PATCH",{discoverable:false,expected_revision:1,purge_epoch:0},owner.token)).status,200);
  assert.equal((await request(path,"POST",pending,owner.token,faceHeaders)).status,409);
  assert.equal((await db.prepare("SELECT COUNT(*) n FROM face_idempotency WHERE request_id=?").bind(pending.request_id).first()).n,0);
  const stored=await db.prepare("SELECT revision,ciphertext FROM face_templates WHERE profile_id=?").bind(id).first();
  assert.equal(stored.revision,1);assert.deepEqual(Buffer.from(stored.ciphertext),before);
  const current=(await (await request(path,"GET",undefined,owner.token)).json()).data;
  assert.equal(current.discoverable,false);assert.equal(current.consent_generation,1);
  const freshConsent=syntheticEnrollment(1);
  assert.equal((await request(path,"POST",freshConsent,owner.token,{"If-Face-Consent-Generation":"1"})).status,200);
});

for(const originOverride of ["https://portfolio-api.example.invalid","http://apppofo-profile-api-staging.synthetic.workers.dev","https://unapproved.synthetic.workers.dev"])
  test("S5-10 remote gate rejects wrong deployment origin: "+new URL(originOverride).host,async t=>{
    const {request}=await fixture(t,{stage:true,originOverride});
    assert.equal((await request("/api/face/config")).status,503);
    const created=await request("/api/profiles","POST",{display_name:"Synthetic",items:[]});
    const id=(await created.json()).data.id;
    assert.equal((await request("/api/profiles/"+id+"/face","POST",syntheticEnrollment())).status,503);
  });

test("production app lifecycle uses signed devices, explicit consent and public encrypted faces",async t=>{
  const {request,db}=await fixture(t,{production:true});
  const owner=await signedDevice(request),scanner=await signedDevice(request);
  const configResponse=await request("/api/me/face/config","GET",undefined,owner.token);
  assert.equal(configResponse.status,200);
  const config=await configResponse.json();
  assert.equal(config.environment,"production");assert.equal(config.decision_version,"production-v1");
  assert.equal((await request("/api/me/face/config","GET",undefined,"")).status,401);
  const draft={display_name:"Synthetic production participant",items:[]};
  const headers={"Idempotency-Key":crypto.randomUUID()};
  const created=await request("/api/me/profiles","POST",draft,owner.token,headers);
  assert.equal(created.status,201);const id=(await created.json()).data.id;
  const replay=await request("/api/me/profiles","POST",draft,owner.token,headers);
  assert.equal((await replay.json()).data.id,id);
  assert.equal((await request("/api/me/profiles/"+id,"GET",undefined,scanner.token)).status,404);
  const path="/api/me/profiles/"+id+"/face";
  const enrollment=syntheticEnrollment();enrollment.consent.policy_version=config.enrollment_policy;
  assert.equal((await request(path,"POST",enrollment,owner.token,faceHeaders)).status,201);
  assert.equal((await request("/api/me/profiles","POST",{display_name:"Synthetic production scanner",items:[]},scanner.token)).status,201);
  assert.equal((await request("/api/me/face/match","POST",syntheticQuery(),scanner.token)).status,403);
  assert.equal((await request("/api/me/face/scan-consent","POST",{consent:true,policy_version:config.scan_policy},scanner.token)).status,204);
  const matched=await request("/api/me/face/match","POST",syntheticQuery(),scanner.token);
  assert.equal(matched.status,200);
  assert.deepEqual(await matched.json(),{matched:true,profile:{id,display_name:draft.display_name,headline:"",business_card_theme:"AURORA",avatar_base64:null},decision_version:"production-v1"});
  assert.equal((await request(path,"PATCH",{discoverable:false,expected_revision:1,purge_epoch:0},owner.token)).status,200);
  const state=(await (await request(path,"GET",undefined,owner.token)).json()).data;
  assert.equal(state.registered,true);assert.equal(state.discoverable,false);
  assert.equal((await db.prepare("SELECT revision FROM face_templates WHERE profile_id=?").bind(id).first()).revision,1);
  assert.deepEqual(await (await request("/api/me/face/match","POST",syntheticQuery(),scanner.token)).json(),{matched:false});
  assert.equal((await request("/api/me/face/scan-consent","DELETE",undefined,scanner.token)).status,204);
  assert.equal((await request("/api/me/face/match","POST",syntheticQuery(),scanner.token)).status,403);
});

test("S5-10 staging config fails closed before capture when secret or matching policy is invalid",async t=>{
  const {request}=await fixture(t,{stage:true,overrides:{APPPOFO_FACE_KEY_V1:"invalid"}});
  assert.equal((await request("/api/face/config")).status,503);
});

async function fixture(t,{face=false,match=false,stage=false,production=false,originOverride=null,overrides={}}={}) {
  if(production) stage=true;
  // Miniflare 5's adapter converts these v4 options to its config/dev schema.
  // This uses local, ephemeral D1 only; it never reaches a Cloudflare account.
  const mf = new Miniflare(convertV4MiniflareOptions({
    cf: false,
    workers: [{
      name: "apppofo-local-test",
      modulesRoot: fileURLToPath(sourceRoot),
      // v5 needs an explicit module manifest, including relative imports.
      modules: moduleNames.map((name) => ({
        type: "ESModule",
        path: fileURLToPath(new URL(name, sourceRoot)),
      })),
      compatibilityDate: "2026-10-13",
      d1Databases: { DB: "apppofo-local-test" },
      bindings: { APPPOFO_API_TOKEN: TOKEN,
        ...((face||stage) ? { APPPOFO_FACE_LOCAL_TEST_MODE:"enabled",
          APPPOFO_FACE_POLICY_VERSION:"synthetic-face-test-v1",
          APPPOFO_FACE_KEY_V1:randomBytes(32).toString("base64url") } : {}),
        ...(match ? { APPPOFO_FACE_MATCH_LOCAL_TEST_MODE:"enabled",
          APPPOFO_FACE_SCAN_POLICY_VERSION:"synthetic-scanner-policy-v1",
          APPPOFO_FACE_MATCH_THRESHOLD_TEST:"0.75",
          APPPOFO_FACE_MATCH_MARGIN_TEST:"0.05" } : {}),
        ...(stage ? {APPPOFO_DEPLOYMENT_STAGE:"staging",APPPOFO_FACE_REMOTE_ENABLED:"enabled",
          APPPOFO_FACE_ALLOWED_HOST:"apppofo-profile-api-staging.synthetic.workers.dev",
          APPPOFO_FACE_SCAN_POLICY_VERSION:"synthetic-scanner-policy-v1",
          APPPOFO_FACE_MATCH_THRESHOLD:"0.75",APPPOFO_FACE_MATCH_MARGIN:"0.05"} : {}),
        ...(production ? {APPPOFO_DEPLOYMENT_STAGE:"production",
          APPPOFO_FACE_ALLOWED_HOST:"portfolio-api.example.invalid",
          APPPOFO_FACE_POLICY_VERSION:"production-face-storage-v1",
          APPPOFO_FACE_SCAN_POLICY_VERSION:"production-face-scan-v1",
          APPPOFO_FACE_MATCH_THRESHOLD:"0.65"} : {}),
        ...overrides },
    }],
  }));
  t.after(() => mf.dispose());
  const db = await mf.getD1Database("DB");
  // D1 exec() treats each line as a query; this migration has multiline DDL.
  // Its statements contain no semicolons inside literals or trigger bodies.
  const statements = schema.replace(/^\s*--[^\r\n]*/gm, "")
    .split(";").map((sql) => sql.trim()).filter(Boolean);
  await db.batch(statements.map((sql) => db.prepare(sql)));
  async function request(path, method = "GET", payload, token = TOKEN, extraHeaders = {}) {
    const host=originOverride??(production?"https://portfolio-api.example.invalid":stage?"https://apppofo-profile-api-staging.synthetic.workers.dev":origin);
    return mf.dispatchFetch(host + path, {
      method,
      headers: {
        ...(token ? { Authorization: "Bearer " + token } : {}),
        ...(payload === undefined ? {} : { "Content-Type": "application/json" }),
        ...extraHeaders,
      },
      ...(payload === undefined ? {} : { body: JSON.stringify(payload) }),
    });
  }
  return { db, request };
}

test("workerd + real local D1 binding: security, CRUD, 45-item limit and reserved GET", async (t) => {
  const { db, request } = await fixture(t);
  const health = await request("/api/health", "GET", undefined, "");
  assert.equal(health.status, 200);
  assert.deepEqual(await health.json(), { status: "ok" });
  assert.equal((await request("/api/profiles", "GET", undefined, "")).status, 401);
  assert.equal((await request("/api/profiles", "GET", undefined, "bad-token")).status, 401);
  assert.equal((await request("/api/profiles?limit=25")).status, 400);
  assert.equal((await request("/api/profiles", "POST",
    { display_name: "테스트", items: [], avatar_path: "nope" })).status, 400);
  assert.equal((await request("/api/profiles", "POST",
    { display_name: "테스트", items: Array.from({length: 46}, (_, i) =>
      ({ item_type: "SKILL", title: "Skill" + i })) })).status, 400);

  const items = Array.from({length: 45}, (_, i) =>
    ({item_type: "SKILL", title: "Skill" + i, sort_order: i}));
  const created = await request("/api/profiles", "POST",
    { display_name: "홍길동", headline: "개발자", items });
  assert.equal(created.status, 201);
  const id = (await created.json()).data.id;
  assert.match(id, /^[0-9a-f-]{36}$/);
  const list = await request("/api/profiles");
  assert.equal(list.status, 200);
  assert.equal((await list.json()).total, 1);

  const updated = await request("/api/profiles/" + id, "PUT",
    { display_name: "수정됨", items });
  assert.equal(updated.status, 200);
  const count = await db.prepare("SELECT COUNT(*) n FROM profile_items WHERE profile_id = ?")
    .bind(id).first();
  assert.equal(count.n, 45);
  // Exercise the student-authored GET through the actual workerd + D1 boundary.
  const detailResponse = await request("/api/profiles/" + id);
  assert.equal(detailResponse.status, 200);
  const detail = (await detailResponse.json()).data;
  assert.equal(detail.id, id);
  assert.equal(detail.display_name, "수정됨");
  assert.equal(detail.items.length, 45);
  assert.deepEqual(detail.items.map((item) => item.sort_order), items.map((item) => item.sort_order));
  assert.equal("face_embeddings" in detail, false);
  assert.equal("avatar_path" in detail, false);
  assert.equal((await request("/api/profiles/" + id, "GET", undefined, "")).status, 401);
  assert.equal((await request("/api/profiles/" + id, "DELETE")).status, 204);
  assert.equal((await request("/api/profiles/" + id)).status, 404);
  assert.equal((await request("/api/profiles/" + id, "DELETE")).status, 404);
  assert.equal((await db.prepare("SELECT COUNT(*) n FROM profile_items").first()).n, 0);
  assert.equal(await db.prepare("SELECT name FROM sqlite_master WHERE name = ?")
    .bind("face_embeddings").first(), null);
});

test("workerd local D1 batch rolls back changes after a foreign-key failure", async (t) => {
  const { db } = await fixture(t);
  const id = "11111111-1111-4111-8111-111111111111";
  await assert.rejects(db.batch([
    db.prepare("INSERT INTO profiles(id, display_name, created_at, updated_at) VALUES(?,?,?,?)")
      .bind(id, "테스트", 1, 1),
    db.prepare("INSERT INTO profile_items(profile_id, item_type, title) VALUES(?,?,?)")
      .bind("missing", "SKILL", "C#"),
  ]));
  assert.equal(await db.prepare("SELECT id FROM profiles WHERE id = ?").bind(id).first(), null);
});

test("workerd + D1 migration: automatic key registration, login, and private mobile data",async(t)=>{
  const {db,request}=await fixture(t);
  const pair=generateKeyPairSync("ec",{namedCurve:"prime256v1"});
  const exported=pair.publicKey.export({format:"jwk"});
  const publicKey={kty:"EC",crv:"P-256",x:exported.x,y:exported.y};
  const init=await request("/api/device/challenge","POST",
    {purpose:"register",public_key:publicKey},"");
  assert.equal(init.status,201);
  const challenge=(await init.json()).challenge_id;
  const thumbprint=createHash("sha256")
    .update("AppPofo-P256-v1\n"+publicKey.x+"\n"+publicKey.y).digest("base64url");
  const registerSig=sign("sha256",
    Buffer.from("AppPofo-v1\nregister\n"+challenge+"\n"+thumbprint),
    pair.privateKey).toString("base64url");
  const created=await request("/api/device/register","POST",
    {challenge_id:challenge,public_key:publicKey,signature:registerSig},"");
  assert.equal(created.status,201);
  const deviceId=(await created.json()).device_id;
  const sessionChallenge=await request("/api/device/challenge","POST",
    {purpose:"session",device_id:deviceId},"");
  assert.equal(sessionChallenge.status,201);
  const nonce=(await sessionChallenge.json()).challenge_id;
  const sig=sign("sha256",Buffer.from("AppPofo-v1\nsession\n"+nonce+"\n"+deviceId),
    pair.privateKey).toString("base64url");
  const session=await request("/api/device/session","POST",
    {challenge_id:nonce,device_id:deviceId,signature:sig},"");
  assert.equal(session.status,200);
  const token=(await session.json()).access_token;
  const list=await request("/api/me/profiles","GET",undefined,token);
  assert.equal(list.status,200);
  assert.equal((await list.json()).total,0);
  const add=await request("/api/me/profiles","POST",
    {display_name:"기기 사용자",items:[{item_type:"SKILL",title:"Unity"}]},token);
  assert.equal(add.status,201);
  const id=(await add.json()).data.id;
  const row=await db.prepare("SELECT owner_device_id FROM profiles WHERE id=?").bind(id).first();
  assert.equal(row.owner_device_id,deviceId);
  const detail=await request("/api/me/profiles/"+id,"GET",undefined,token);
  assert.equal(detail.status,200);
  assert.equal((await detail.json()).data.items[0].title,"Unity");
  assert.equal((await request("/api/me/profiles/"+id,"GET",undefined,"")).status,401);
  assert.equal((await request("/api/me/profiles/"+id,"DELETE",undefined,token)).status,204);
  assert.equal((await db.prepare("SELECT COUNT(*) n FROM profile_items WHERE profile_id=?")
    .bind(id).first()).n,0);
});


test("workerd real local D1 0004: encrypted face registration CAS, replay, delete", async(t) => {
  const {db,request}=await fixture(t,{face:true});
  const create=await request("/api/profiles","POST",{display_name:"실험용 프로필",items:[]});
  assert.equal(create.status,201);
  const id=(await create.json()).data.id;
  const face="/api/profiles/"+id+"/face";
  const init=await request(face);
  assert.equal(init.status,200);
  assert.equal((await init.json()).data.revision,0);

  const samples=new Uint8Array(1536), view=new DataView(samples.buffer);
  for(let pose=0;pose<3;pose++)view.setFloat32((pose*128+pose)*4,1,true);
  const payload={request_id:crypto.randomUUID(),expected_revision:0,purge_epoch:0,
    model_version:"sface_2021dec",embeddings_b64url:encodeFaceBase64Url(samples),
    consent:{policy_version:"synthetic-face-test-v1",storage_opt_in:true,
      discoverable_opt_in:false,confirmed_at:Date.now(),subject_present:true}};
  const enrolled=await request(face,"POST",payload);
  assert.equal(enrolled.status,201,JSON.stringify(await enrolled.json()));
  const record=await db.prepare("SELECT length(ciphertext) AS bytes,revision FROM face_templates WHERE profile_id=?")
    .bind(id).first();
  assert.equal(record.bytes,1552);
  assert.equal(record.revision,1);
  assert.equal((await request(face,"POST",payload)).status,200);
  const current=await (await request(face)).json();
  assert.equal(current.data.revision,1);
  assert.equal(current.data.registered,true);
  assert.equal(current.data.discoverable,false);
  const stale={...payload,request_id:crypto.randomUUID()};
  assert.equal((await request(face,"POST",stale)).status,409);
  const del={request_id:crypto.randomUUID(),expected_revision:1,purge_epoch:0};
  assert.equal((await request(face,"DELETE",del)).status,204);
  assert.equal((await request(face,"DELETE",del)).status,204);
  assert.equal((await request(face,"POST",payload)).status,409);
  const gone=await (await request(face)).json();
  assert.equal(gone.data.revision,2);
  assert.equal(gone.data.registered,false);
  assert.equal((await db.prepare("SELECT count(*) AS n FROM face_templates").first()).n,0);
  assert.equal((await db.prepare("SELECT count(*) AS n FROM face_operation_guards").first()).n,0);
  const detail=await (await request("/api/profiles/"+id)).json();
  assert.equal(detail.data.display_name,"실험용 프로필");
});


test("workerd D1: admin two-step purge deletes only faces and rejects stale enrollment",async(t)=>{
  const {db,request}=await fixture(t,{face:true});
  const create=await request("/api/profiles","POST",{display_name:"보존할 프로필",items:[
    {item_type:"SKILL",title:"Unity"}]});
  assert.equal(create.status,201);
  const id=(await create.json()).data.id,face="/api/profiles/"+id+"/face";
  const bytes=new Uint8Array(1536),view=new DataView(bytes.buffer);
  for(let i=0;i<3;i++)view.setFloat32((i*128+i)*4,1,true);
  const payload={request_id:crypto.randomUUID(),expected_revision:0,purge_epoch:0,
    model_version:"sface_2021dec",embeddings_b64url:encodeFaceBase64Url(bytes),
    consent:{policy_version:"synthetic-face-test-v1",storage_opt_in:true,
      discoverable_opt_in:true,confirmed_at:Date.now(),subject_present:true}};
  assert.equal((await request(face,"POST",payload)).status,201);
  const prepPath="/api/admin/face-templates/purge/prepare";
  const confirmPath="/api/admin/face-templates/purge/confirm";
  assert.equal((await request(prepPath,"POST",undefined,"")).status,401);
  const preparation=await request(prepPath,"POST");
  assert.equal(preparation.status,201);
  const prepared=(await preparation.json()).data;
  assert.equal(prepared.expected_count,1);
  assert.equal(prepared.purge_epoch,0);
  assert.equal((await request(confirmPath,"POST",{...prepared,
    confirmation_token:crypto.randomUUID()})).status,400,
    "Unapproved extra fields must be rejected.");
  const confirmation={operation_id:prepared.operation_id,
    confirmation_token:prepared.confirmation_token,
    expected_count:prepared.expected_count,purge_epoch:prepared.purge_epoch};
  assert.equal((await request(confirmPath,"POST",confirmation)).status,200);
  assert.equal((await request(confirmPath,"POST",confirmation)).status,409);
  const state=(await (await request(face)).json()).data;
  assert.equal(state.registered,false);
  assert.equal(state.discoverable,false);
  assert.equal(state.revision,2);
  assert.equal(state.purge_epoch,1);
  assert.equal((await db.prepare("SELECT count(*) AS n FROM face_templates").first()).n,0);
  assert.equal((await request(face,"POST",payload)).status,409);
  assert.equal((await request(face,"POST",{...payload,
    request_id:crypto.randomUUID(),expected_revision:0})).status,409);
  assert.equal((await (await request("/api/profiles/"+id)).json()).data.display_name,
    "보존할 프로필");
  assert.equal((await db.prepare("SELECT count(*) AS n FROM profile_items").first()).n,1);

  // A new, explicit consent is required after purge; stale epoch 0 is invalid.
  const updated={...payload,request_id:crypto.randomUUID(),
    expected_revision:2,purge_epoch:1,consent:{...payload.consent,confirmed_at:Date.now()}};
  assert.equal((await request(face,"POST",updated)).status,200);
  assert.equal((await (await request(face)).json()).data.revision,3);
});


// Real Worker runtime and D1: never needs the deployed Cloudflare account.
test("workerd S5-09-A signed scanner searches only explicitly public encrypted faces",async(t)=>{
  const {db,request}=await fixture(t,{face:true,match:true});

  const pair=generateKeyPairSync("ec",{namedCurve:"prime256v1"});
  const pub=pair.publicKey.export({format:"jwk"});
  const jwk={kty:"EC",crv:"P-256",x:pub.x,y:pub.y};
  const start=await request("/api/device/challenge","POST",
    {purpose:"register",public_key:jwk},"");
  assert.equal(start.status,201);
  const registerChallenge=(await start.json()).challenge_id;
  const thumbprint=createHash("sha256").update(
    "AppPofo-P256-v1\n"+jwk.x+"\n"+jwk.y).digest("base64url");
  const registerSignature=sign("sha256",Buffer.from(
    "AppPofo-v1\nregister\n"+registerChallenge+"\n"+thumbprint),
    pair.privateKey).toString("base64url");
  const registered=await request("/api/device/register","POST",
    {challenge_id:registerChallenge,public_key:jwk,signature:registerSignature},"");
  assert.equal(registered.status,201);
  const deviceId=(await registered.json()).device_id;
  const sessionChallenge=await request("/api/device/challenge","POST",
    {purpose:"session",device_id:deviceId},"");
  assert.equal(sessionChallenge.status,201);
  const challenge=(await sessionChallenge.json()).challenge_id;
  const sessionSig=sign("sha256",
    Buffer.from("AppPofo-v1\nsession\n"+challenge+"\n"+deviceId),
    pair.privateKey).toString("base64url");
  const session=await request("/api/device/session","POST",
    {challenge_id:challenge,device_id:deviceId,signature:sessionSig},"");
  assert.equal(session.status,200);
  const userToken=(await session.json()).access_token;

  const own=await request("/api/me/profiles","POST",
    {display_name:"scanner",items:[]},userToken);
  assert.equal(own.status,201);
  const candidate=await request("/api/profiles","POST",{
    display_name:"synthetic match target",headline:"allowed field",
    email:"private@example.invalid",items:[]});
  assert.equal(candidate.status,201);
  const id=(await candidate.json()).data.id;
  const data=new Uint8Array(1536);
  const view=new DataView(data.buffer);
  for(let p=0;p<3;p++)view.setFloat32((p*128+p)*4,1,true);
  const enrolled=await request("/api/profiles/"+id+"/face","POST",{
    request_id:crypto.randomUUID(),expected_revision:0,purge_epoch:0,
    model_version:"sface_2021dec",embeddings_b64url:encodeFaceBase64Url(data),
    consent:{policy_version:"synthetic-face-test-v1",storage_opt_in:true,
      discoverable_opt_in:true,confirmed_at:Date.now(),subject_present:true},
  });
  assert.equal(enrolled.status,201);

  const query=new Uint8Array(512);
  new DataView(query.buffer).setFloat32(0,1,true);
  const payload={request_id:crypto.randomUUID(),
    model_version:"sface_2021dec",embedding_b64url:encodeFaceBase64Url(query)};
  const url="/api/me/face/match";
  assert.equal((await request(url,"POST",payload,userToken)).status,403);
  const consent=await request("/api/me/face/scan-consent","POST",{
    consent:true,policy_version:"synthetic-scanner-policy-v1",
  },userToken);
  assert.equal(consent.status,204);
  const res=await request(url,"POST",payload,userToken);
  assert.equal(res.status,200);
  assert.deepEqual(await res.json(),{
    matched:true,profile:{id,display_name:"synthetic match target",headline:"allowed field",business_card_theme:"AURORA",avatar_base64:null},
    decision_version:"local-synthetic-v1",
  });
  const summaryPath="/api/me/face/profiles/"+id+"/summary";
  const summary=await request(summaryPath,"GET",undefined,userToken);
  assert.equal(summary.status,200);
  const publicData=(await summary.json()).data;
  assert.equal(publicData.id,id);assert.equal(publicData.display_name,"synthetic match target");
  assert.deepEqual(publicData.skills,[]);assert.deepEqual(publicData.links,[]);assert.equal(publicData.project,null);
  assert.ok(!JSON.stringify(publicData).includes("private@example.invalid"));
  assert.equal((await request(summaryPath,"GET",undefined,TOKEN)).status,401);
  assert.equal((await request(url,"POST",payload,TOKEN)).status,401);
  const deletion=await request("/api/profiles/"+id+"/face","DELETE",{
    request_id:crypto.randomUUID(),expected_revision:1,purge_epoch:0,
  });
  assert.equal(deletion.status,204);
  const after=await request(url,"POST",payload,userToken);
  assert.equal(after.status,200);
  assert.deepEqual(await after.json(),{matched:false});
  assert.equal((await request(summaryPath,"GET",undefined,userToken)).status,404);
  assert.equal((await db.prepare("SELECT count(*) AS n FROM face_scan_grants").first()).n,1);
});

import test from "node:test";
import assert from "node:assert/strict";
import { createHash, randomBytes } from "node:crypto";
import { DatabaseSync } from "node:sqlite";
import { readFileSync } from "node:fs";
import worker from "../src/index.js";
import { encodeFaceBase64Url } from "../src/face-validation.js";
import { decryptFaceSet } from "../src/face-crypto.js";

const id = "11111111-1111-4111-8111-111111111111";
const policy = "only-synthetic-faces-policy-v1";
const token = "synthetic-face-test-administrator-token-1234567890";
const secret = randomBytes(32).toString("base64url");
const schemaFiles = ["0001_profiles.sql","0002_device_ownership.sql",
  "0003_profile_connections.sql","0004_face_enrollment.sql"];
function vectors(pose = 0) {
  const bytes = new Uint8Array(1536);
  const view = new DataView(bytes.buffer);
  for(let i=0;i<3;i++)view.setFloat32((i*128+(pose+i)%128)*4,1,true);
  return bytes;
}
const requestId = () => crypto.randomUUID();
function enrollment(revision=0,epoch=0,vectorsBytes=vectors()) {
  return {
    request_id: requestId(),expected_revision:revision,purge_epoch:epoch,
    model_version:"sface_2021dec",embeddings_b64url:encodeFaceBase64Url(vectorsBytes),
    consent:{policy_version:policy,storage_opt_in:true,discoverable_opt_in:false,
      confirmed_at:Date.now(),subject_present:true},
  };
}
function fixture({ enabled=true, encryptionKey=true }={}) {
  const db = new DatabaseSync(":memory:");
  db.exec("PRAGMA foreign_keys=ON;");
  db.exec(schemaFiles.map(name =>
    readFileSync(new URL("../migrations/"+name,import.meta.url),"utf8")).join("\n"));
  const blob = val => Array.isArray(val) ? Uint8Array.from(val) : val;
  const prepare = (sql,args=[]) => ({
    bind: (...p) => prepare(sql,p),
    first: async()=> db.prepare(sql).get(...args.map(blob)) ?? null,
    all: async()=> ({results:db.prepare(sql).all(...args.map(blob))}),
    run: async()=> {
      const result = db.prepare(sql).run(...args.map(blob));
      return {meta:{changes:Number(result.changes)},results:[]};
    },
  });
  const DB = {prepare:sql=>prepare(sql),async batch(commands) {
    db.exec("BEGIN");
    try {
      const result=[];
      for(const cmd of commands) result.push(await cmd.run());
      db.exec("COMMIT");
      return result;
    } catch(e) {db.exec("ROLLBACK"); throw e;}
  }};
  db.prepare("INSERT INTO profiles(id,display_name,created_at,updated_at) VALUES(?,?,?,?)")
    .run(id,"가상 사용자",1,1);
  const env = {DB,APPPOFO_API_TOKEN:token,APPPOFO_FACE_POLICY_VERSION:policy,
    ...(enabled?{APPPOFO_FACE_LOCAL_TEST_MODE:"enabled"}:{}),
    ...(encryptionKey?{APPPOFO_FACE_KEY_V1:secret}:{})};
  async function call(path,method="GET",body,authorization=token) {
    return worker.fetch(new Request("https://local.test"+path,{
      method,headers:{
        ...(authorization?{Authorization:"Bearer "+authorization}:{}),
        ...(body!==undefined?{"Content-Type":"application/json"}:{}),
      },...(body===undefined?{}:{body:JSON.stringify(body)}),
    }),env);
  }
  return {db,DB,env,call};
}
const face = "/api/profiles/"+id+"/face";
const status = async f => (await (await f.call(face)).json()).data;

test("0004 migration is additive, enforces FK and preserves original text records",()=>{
  const f=fixture();
  try {
    assert.equal(f.db.prepare("SELECT display_name FROM profiles WHERE id=?").get(id).display_name,
      "가상 사용자");
    for(const table of ["face_templates","face_consent_state","face_idempotency",
      "face_consent_audit","face_operation_guards","face_system_state","face_admin_operations"])
      assert.equal(f.db.prepare("SELECT 1 FROM sqlite_master WHERE name=?").get(table)["1"],1);
    assert.equal(f.db.prepare("SELECT purge_epoch FROM face_system_state").get().purge_epoch,0);
    assert.throws(()=>f.db.prepare("INSERT INTO face_consent_state(profile_id) VALUES(?)")
      .run("missing-profile"),/constraint/i);
  } finally {f.db.close();}
});

test("encrypted 3-pose enroll GET, idempotent replay, revision-CAS and safe delete",async()=>{
  const f=fixture();
  try {
    assert.deepEqual(await status(f),{
      registered:false,revision:0,purge_epoch:0,model_version:null,
      storage_consent:false,discoverable:false,updated_at:null,
    });
    const first = enrollment();
    const response = await f.call(face,"POST",first);
    assert.equal(response.status,201,await response.text?.());
    // Response bodies can only be consumed once, so request from status for revision.
    const state1=await status(f);
    assert.equal(state1.registered,true);
    assert.equal(state1.revision,1);
    assert.equal(state1.discoverable,false);
    const saved=f.db.prepare("SELECT * FROM face_templates WHERE profile_id=?").get(id);
    assert.equal(saved.ciphertext.length,1552);
    assert.equal(saved.nonce.length,12);
    assert.equal(saved.ciphertext.includes(Buffer.from(first.embeddings_b64url)),false);
    assert.deepEqual(await decryptFaceSet({
      cipherSuite:saved.cipher_suite,nonce:new Uint8Array(saved.nonce),
      ciphertext:new Uint8Array(saved.ciphertext),revision:saved.revision,
      modelVersion:saved.model_version,keyVersion:saved.key_version,
    },{profileId:id,secretBase64Url:secret}),vectors());

    assert.equal((await f.call(face,"POST",first)).status,200);
    assert.equal((await status(f)).revision,1);
    // The same UUID used for a different operation must not satisfy a stale CAS.
    assert.equal((await f.call(face,"DELETE",{
      request_id:first.request_id,expected_revision:0,purge_epoch:0,
    })).status,409);
    assert.equal((await status(f)).registered,true);
    const receipt=await (await f.call(face+"?request_id="+first.request_id)).json();
    assert.equal(receipt.data.request_status,"COMMITTED");
    assert.equal(receipt.data.request_revision,1);
    assert.equal((await f.call(face,"POST",{...first,expected_revision:1})).status,409);
    const stale=enrollment(0,0,vectors(1));
    assert.equal((await f.call(face,"POST",stale)).status,409);

    const replace=enrollment(1,0,vectors(1));
    assert.equal((await f.call(face,"POST",replace)).status,200);
    assert.equal((await status(f)).revision,2);
    const remove={request_id:requestId(),expected_revision:2,purge_epoch:0};
    assert.equal((await f.call(face,"DELETE",remove)).status,204);
    assert.equal((await f.call(face,"DELETE",remove)).status,204);
    const s=await status(f);
    assert.equal(s.revision,3);
    assert.equal(s.registered,false);
    assert.equal(s.storage_consent,false);
    assert.equal(f.db.prepare("SELECT count(*) AS n FROM face_templates").get().n,0);
    assert.equal((await f.call(face,"POST",replace)).status,409,
      "A stale enrollment receipt cannot claim an active face after deletion.");
    assert.equal((await status(f)).registered,false);
    assert.equal((await f.call(face,"POST",enrollment(2,0))).status,409);
    assert.equal(f.db.prepare("SELECT display_name FROM profiles WHERE id=?").get(id).display_name,
      "가상 사용자");
    assert.equal(f.db.prepare("SELECT count(*) AS n FROM face_operation_guards").get().n,0);
    assert.equal(f.db.prepare("SELECT count(*) AS n FROM face_consent_audit").get().n,3);
  } finally {f.db.close();}
});

test("local-only feature gate is required; no schema write without policy or key",async()=>{
  for(const opts of [{enabled:false},{encryptionKey:false}]){
    const f=fixture(opts);
    try{
      const response=await f.call(face,"POST",enrollment());
      assert.equal(response.status,503);
      assert.equal(f.db.prepare("SELECT count(*) AS n FROM face_templates").get().n,0);
      assert.equal(f.db.prepare("SELECT count(*) AS n FROM face_idempotency").get().n,0);
    }finally{f.db.close();}
  }
});

test("failed CAS aborts entire D1 batch, does not leave receipt, audit, face or assertion",async()=>{
  const f=fixture();
  try {
    // Change state between the application-side read and transactional CAS.
    const first=enrollment();
    const now=Date.now();
    f.db.prepare("INSERT INTO face_consent_state(profile_id,revision,purge_epoch) VALUES(?,1,0)")
      .run(id);
    assert.equal((await f.call(face,"POST",first)).status,409);
    for(const table of ["face_templates","face_idempotency","face_consent_audit",
      "face_operation_guards"])
      assert.equal(f.db.prepare("SELECT count(*) AS n FROM "+table).get().n,0);
    assert.equal((await status(f)).revision,1);
  }finally{f.db.close();}
});

test("GET and mutation reject missing auth and disabled profile before exposing data",async()=>{
  const f=fixture();
  try {
    assert.equal((await f.call(face,"GET",undefined,"")).status,401);
    assert.equal((await f.call(face,"POST",enrollment(),"")).status,401);
    f.db.prepare("UPDATE profiles SET is_active=0 WHERE id=?").run(id);
    assert.equal((await f.call(face,"POST",enrollment())).status,409);
    assert.equal((await status(f)).registered,false);
  }finally{f.db.close();}
});

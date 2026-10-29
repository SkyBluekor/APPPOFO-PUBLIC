import test from "node:test";
import assert from "node:assert/strict";
import { createLocalScanSession, checkLocalWorker } from "../tools/face_scan_local_session.mjs";

const env={
  APPPOFO_FACE_LOCAL_TEST_MODE:"enabled",
  APPPOFO_FACE_MATCH_LOCAL_TEST_MODE:"enabled",
  APPPOFO_FACE_SCAN_POLICY_VERSION:"local-face-scan-test-v1",
};

test("S5-09-B synthetic local scanner obtains real signed-device contract and grants opt-in",async()=>{
  const calls=[];
  const device="11111111-1111-4111-8111-111111111111";
  const mock=async(path,payload,bearer)=>{
    calls.push({path,payload,bearer});
    if(path==="/api/device/challenge")
      return {challenge_id:payload.purpose==="register"
        ?"22222222-2222-4222-8222-222222222222"
        :"33333333-3333-4333-8333-333333333333"};
    if(path==="/api/device/register") return {device_id:device};
    if(path==="/api/device/session")
      return {access_token:"appdev_"+"a".repeat(43),expires_at:Date.now()+1800000};
    if(path==="/api/me/profiles") return {data:{id:"44444444-4444-4444-8444-444444444444"}};
    if(path==="/api/me/face/scan-consent") return null;
    throw new Error("Unexpected URL: "+path);
  };
  const stages=[];
  const session=await createLocalScanSession(env,mock,stage=>stages.push(stage));
  assert.deepEqual(stages,[
    "register-challenge","device-registration","session-challenge",
    "signed-session","scanner-text-profile","scan-consent",
  ]);
  assert.equal(session.device_id,device);
  assert.equal(session.policy_version,"local-face-scan-test-v1");
  assert.equal(session.access_token,"appdev_"+"a".repeat(43));
  assert.deepEqual(calls.map(c=>c.path),[
    "/api/device/challenge","/api/device/register",
    "/api/device/challenge","/api/device/session",
    "/api/me/profiles","/api/me/face/scan-consent",
  ]);
  assert.equal(calls[0].payload.public_key.kty,"EC");
  assert.equal(calls[0].payload.public_key.crv,"P-256");
  assert.match(calls[1].payload.signature,/^[A-Za-z0-9_-]+$/);
  assert.equal(calls[4].payload.display_name,"로컬 얼굴검색 테스트 기기");
  assert.equal(calls[5].payload.consent,true);
  assert.equal(calls[5].payload.policy_version,env.APPPOFO_FACE_SCAN_POLICY_VERSION);
  assert.equal(calls.every(c=>!JSON.stringify(c.payload).includes("embedding")),true);
});

test("S5-09-B scanner helper refuses non-local-match settings without network",async()=>{
  let called=0;
  await assert.rejects(
    createLocalScanSession({...env,APPPOFO_FACE_MATCH_LOCAL_TEST_MODE:"off"},async()=>{
      called++;return null;
    }),/Enable optional local match/);
  assert.equal(called,0);
});

test("S5-09-B local preflight is read-only and fails before device creation", async()=>{
  const urls=[],methods=[];
  const ok=await checkLocalWorker(async(url,options)=>{
    urls.push(url); methods.push(options.method); return {ok:true,status:200};
  });
  assert.equal(ok,true);
  assert.deepEqual(urls,["http://127.0.0.1:8787/api/health"]);
  assert.deepEqual(methods,["GET"]);
  await assert.rejects(
    checkLocalWorker(async()=>{throw new Error("synthetic offline");}),
    /Local Worker health check timed out or was unreachable/);
  await assert.rejects(
    checkLocalWorker(async()=>({ok:false,status:503})),
    /health check failed \(HTTP 503\)/);
});

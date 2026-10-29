import { matchTestSettings } from "../src/face-match.js";
import test from "node:test";
import assert from "node:assert/strict";
import { stagingConfig } from "../tools/prepare_staging.mjs";
import { runStagingSmoke } from "../tools/staging_smoke.mjs";
test("S5-10 synthetic staging runner rejects production and missing approval before network",async()=>{
  let requests=0;const fetchFn=async()=>{requests++;throw new Error("unexpected network");};
  await assert.rejects(runStagingSmoke({baseUrl:"https://portfolio-api.example.invalid",confirmed:true,fetchFn}));
  await assert.rejects(runStagingSmoke({baseUrl:"https://apppofo-profile-api-staging.synthetic.workers.dev",fetchFn}));
  assert.equal(requests,0);
});
test("S5-10 staging configuration is separate, defaults closed and never embeds secrets",()=>{
  const id="11111111-1111-4111-8111-111111111111",host="apppofo-profile-api-staging.synthetic.workers.dev";
  const config=stagingConfig(id,host);
  assert.equal(config.name,"apppofo-profile-api-staging");
  assert.equal(config.d1_databases[0].database_id,id);
  assert.equal(config.vars.APPPOFO_FACE_REMOTE_ENABLED,"disabled");
  assert.equal(config.vars.APPPOFO_FACE_MATCH_THRESHOLD,"0.75");
  assert.equal(config.vars.APPPOFO_FACE_MATCH_MARGIN,"0.05");
  assert.equal(config.vars.APPPOFO_FACE_LOCAL_TEST_MODE,undefined);
  assert.deepEqual(config.secrets.required,["APPPOFO_API_TOKEN","APPPOFO_FACE_KEY_V1"]);
  assert.equal(config.vars.APPPOFO_API_TOKEN,undefined);
  assert.equal(config.vars.APPPOFO_FACE_KEY_V1,undefined);
  assert.equal(stagingConfig(id,host,{enableFaces:true}).vars.APPPOFO_FACE_REMOTE_ENABLED,"enabled");
  assert.throws(()=>stagingConfig("00000000-0000-4000-8000-000000000001",host),/NEW staging/);
  assert.throws(()=>stagingConfig("00000000-0000-4000-8000-000000000001",host),/NEW staging/);
  assert.throws(()=>stagingConfig(id,"portfolio-api.example.invalid"),/separate staging/);
  assert.throws(()=>stagingConfig(id,"https://"+host),/separate staging/);
});

test("staging comparison permits threshold 0.65 while preserving the 0.05 margin floor",()=>{
  assert.equal(matchTestSettings({APPPOFO_FACE_MATCH_THRESHOLD:"0.75",APPPOFO_FACE_MATCH_MARGIN:"0.05"},true).decisionVersion,"staging-unvalidated-v1");
  assert.deepEqual(matchTestSettings({APPPOFO_FACE_MATCH_THRESHOLD:"0.65",APPPOFO_FACE_MATCH_MARGIN:"0.05"},true),
    {threshold:0.65,margin:0.05,decisionVersion:"staging-unvalidated-v1"});
  assert.throws(()=>matchTestSettings({APPPOFO_FACE_MATCH_THRESHOLD:"0.649",APPPOFO_FACE_MATCH_MARGIN:"0.05"},true));
  assert.throws(()=>matchTestSettings({APPPOFO_FACE_MATCH_THRESHOLD:"0.65",APPPOFO_FACE_MATCH_MARGIN:"0.04"},true));
});

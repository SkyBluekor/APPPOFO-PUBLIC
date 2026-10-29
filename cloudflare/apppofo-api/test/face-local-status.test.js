import test from "node:test";
import assert from "node:assert/strict";
import { localFaceStatus } from "../tools/face_local_status.mjs";

const uuid = "d8c9c066-627c-42f9-bb39-6e521a74dde3";
const auth = {
  APPPOFO_FACE_LOCAL_TEST_MODE: "enabled",
  APPPOFO_API_TOKEN: "synthetic-dev-secret-not-an-actual-user-token-1234",
};

test("local face status only requests loopback, without any write or secret output", async () => {
  let calls = 0;
  const status = await localFaceStatus(uuid, auth, async (url, init) => {
    calls++;
    assert.equal(url, "http://127.0.0.1:8787/api/profiles/" + uuid + "/face");
    assert.equal(init.method, "GET");
    assert.equal(init.redirect, "error");
    assert.equal(init.headers.Authorization, "Bearer " + auth.APPPOFO_API_TOKEN);
    assert.equal(init.body, undefined);
    return {status: 200, json: async () => ({
      data: {registered:true, revision:3, purge_epoch:0, model_version:"sface_2021dec",
        discoverable:false, ciphertext:"DO_NOT_RETURN"},
    })};
  });
  assert.equal(calls,1);
  assert.deepEqual(status,{
    uuid,registered:true,revision:3,purge_epoch:0,model_version:"sface_2021dec",discoverable:false,
  });
  assert.equal(JSON.stringify(status).includes("ciphertext"), false);
});

test("local status fails closed for unapproved settings, bad IDs or bad replies", async () => {
  let calls=0;
  const fake=async () => {calls++; return {status:200,json:async()=>({data:{revision:1}})};};
  await assert.rejects(localFaceStatus("wrong",auth,fake),/UUID/);
  await assert.rejects(localFaceStatus(uuid,{...auth,APPPOFO_FACE_LOCAL_TEST_MODE:"off"},fake),/authorization/);
  assert.equal(calls,0);
  await assert.rejects(localFaceStatus(uuid,auth,fake),/Malformed/);
  await assert.rejects(localFaceStatus(uuid,auth,async()=>({status:401})),/HTTP 401/);
});

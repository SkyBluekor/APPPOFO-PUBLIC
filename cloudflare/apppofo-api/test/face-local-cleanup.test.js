import test from "node:test";
import assert from "node:assert/strict";
import { deleteLocalFace } from "../tools/face_local_cleanup.mjs";

const id = "d8c9c066-627c-42f9-bb39-6e521a74dde3";
const vars = {
  APPPOFO_FACE_LOCAL_TEST_MODE: "enabled",
  APPPOFO_API_TOKEN: "synthetic-test-token-not-for-real-use-1234567890",
};
const response = (status, data) => ({
  status, json: async () => ({data}),
});

test("explicit local cleanup checks revision and verifies deletion, never calls production", async () => {
  const calls = [];
  const fakeFetch = async (url, options) => {
    calls.push({url, method: options.method});
    assert.equal(url, "http://127.0.0.1:8787/api/profiles/" + id + "/face");
    assert.equal(options.redirect, "error");
    assert.equal(options.headers.Authorization, "Bearer " + vars.APPPOFO_API_TOKEN);
    if (calls.length === 1) return response(200, {
      registered:true, revision:7, purge_epoch:2,
    });
    if (calls.length === 2) {
      assert.equal(options.method, "DELETE");
      const body = JSON.parse(options.body);
      assert.equal(body.expected_revision, 7);
      assert.equal(body.purge_epoch, 2);
      assert.match(body.request_id, /^[0-9a-f-]{36}$/);
      return {status:204};
    }
    return response(200, {registered:false,revision:8,purge_epoch:2});
  };
  assert.equal(await deleteLocalFace(id, vars, fakeFetch), "DELETED");
  assert.deepEqual(calls.map(c => c.method), ["GET","DELETE","GET"]);
});

test("local cleanup never deletes when already empty or unauthorized", async () => {
  let calls=0;
  const empty = async () => { calls++; return response(200, {
    registered:false,revision:2,purge_epoch:0
  }); };
  assert.equal(await deleteLocalFace(id,vars,empty), "ALREADY_EMPTY");
  assert.equal(calls,1);

  calls=0;
  await assert.rejects(deleteLocalFace(id,vars,async () => {
    calls++; return response(401,{});
  }), /HTTP 401/);
  assert.equal(calls,1);
});

test("local cleanup rejects invalid UUID, missing test approval and ambiguous completion", async () => {
  let fetchCalls=0;
  const fake=async () => { fetchCalls++; return response(200,{
    registered:true, revision:0,purge_epoch:0
  }); };
  await assert.rejects(deleteLocalFace("not-a-uuid",vars,fake), /canonical local UUID/);
  await assert.rejects(deleteLocalFace(id,{...vars,
    APPPOFO_FACE_LOCAL_TEST_MODE:"disabled"},fake), /not configured/);
  assert.equal(fetchCalls,0);
  let count=0;
  await assert.rejects(deleteLocalFace(id,vars,async () => {
    count++;
    return count===1 ? response(200,{registered:true,revision:1,purge_epoch:0}) :
      {status:409,json:async()=>({})};
  }), /HTTP 409/);
  assert.equal(count,2);
});

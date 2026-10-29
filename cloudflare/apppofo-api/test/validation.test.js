import test from "node:test";
import assert from "node:assert/strict";
import { validateProfile, parsePage, InputError, validCloudId } from "../src/validation.js";

test("profile accepts text and SKILL/PROJECT/LINK only", () => {
  const result = validateProfile({
    display_name: "  이태우 ", items: [
      {item_type: "SKILL", title: " C# "},
      {item_type: "PROJECT", title: "AppPofo"},
      {item_type: "LINK", title: "GitHub"}
    ]
  });
  assert.equal(result.display_name, "이태우");
  assert.equal(result.items.length, 3);
  assert.equal(result.is_active, 1);
});

test("face data, local id and avatar upload are denied", () => {
  for(const key of ["embedding_blob", "avatar_path", "face_embeddings", "id"]) {
    assert.throws(() => validateProfile({display_name:"A",items:[],[key]:"x"}),InputError);
  }
});

test("full replacement requires explicit items", () => {
  assert.throws(()=>validateProfile({display_name:"A"}), InputError);
  assert.throws(()=>validateProfile({display_name:"A",items:[{item_type:"SKILL",title:""}]}),InputError);
});

test("pagination bounded to 24 and cloud ID UUID", () => {
  assert.deepEqual(parsePage(new URLSearchParams()), {limit:12,offset:0});
  assert.throws(()=>parsePage(new URLSearchParams("limit=25")),InputError);
  assert.equal(validCloudId("11111111-1111-4111-8111-111111111111"),true);
  assert.equal(validCloudId("123"),false);
});

test("Workers Free D1 query budget: 45 items accepted, 46 rejected", () => {
  const item = (i) => ({ item_type: "SKILL", title: "Skill" + i });
  const atLimit = { display_name: "테스트", items: Array.from({ length: 45 }, (_, i) => item(i)) };
  const tooMany = { display_name: "테스트", items: Array.from({ length: 46 }, (_, i) => item(i)) };
  assert.equal(validateProfile(atLimit).items.length, 45);
  assert.throws(() => validateProfile(tooMany), InputError);
});

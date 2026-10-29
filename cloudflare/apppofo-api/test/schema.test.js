import test from "node:test";
import assert from "node:assert/strict";
import { DatabaseSync } from "node:sqlite";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const migration = ["0001_profiles.sql","0002_device_ownership.sql","0003_profile_connections.sql"]
  .map(file => readFileSync(fileURLToPath(new URL("../migrations/"+file,import.meta.url)),"utf8")).join("\n");

test("production upgrade from 0002 preserves profiles, items, device identities and ownership",()=>{
 const db=new DatabaseSync(':memory:');db.exec('PRAGMA foreign_keys=ON');
 try{
  const read=name=>readFileSync(new URL('../migrations/'+name,import.meta.url),'utf8');
  db.exec(read('0001_profiles.sql')+read('0002_device_ownership.sql'));
  db.prepare('INSERT INTO device_identities(id,public_key_jwk,key_thumbprint,created_at) VALUES(?,?,?,?)').run('synthetic-device','{}','synthetic-thumb',1);
  db.prepare('INSERT INTO profiles(id,display_name,owner_device_id,created_at,updated_at) VALUES(?,?,?,?,?)').run('synthetic-profile','Synthetic preserved','synthetic-device',1,1);
  db.prepare('INSERT INTO profile_items(profile_id,item_type,title) VALUES(?,?,?)').run('synthetic-profile','SKILL','Preserved skill');
  const before={profiles:db.prepare('SELECT * FROM profiles').all(),items:db.prepare('SELECT * FROM profile_items').all(),devices:db.prepare('SELECT * FROM device_identities').all()};
  for(const name of ['0003_profile_connections.sql','0004_face_enrollment.sql','0005_face_scan_grants.sql','0006_staging_integration.sql','0007_profile_avatars.sql']) db.exec(read(name));
  assert.deepEqual(db.prepare('SELECT * FROM profiles').all(),before.profiles);
  assert.deepEqual(db.prepare('SELECT * FROM profile_items').all(),before.items);
  assert.deepEqual(db.prepare('SELECT * FROM device_identities').all(),before.devices);
  assert.equal(db.prepare('SELECT COUNT(*) n FROM profile_devices WHERE profile_id=? AND device_id=? AND revoked_at IS NULL').get('synthetic-profile','synthetic-device').n,1);
  assert.equal(db.prepare('SELECT COUNT(*) n FROM face_templates').get().n,0);
  assert.equal(db.prepare('SELECT COUNT(*) n FROM face_scan_grants').get().n,0);
  assert.equal(db.prepare('SELECT COUNT(*) n FROM profile_avatars').get().n,0);
 }finally{db.close()}
});

test("D1 schema accepts profiles and enforces cascade locally", () => {
  const db = new DatabaseSync(":memory:");
  try {
    db.exec("PRAGMA foreign_keys=ON;");
    db.exec(migration);
    db.prepare("INSERT INTO profiles(id,display_name,created_at,updated_at) VALUES(?,?,?,?)")
      .run("cloud-id","홍길동",1,1);
    db.prepare("INSERT INTO profile_items(profile_id,item_type,title) VALUES(?,?,?)")
      .run("cloud-id","SKILL","Python");
    assert.equal(db.prepare("SELECT count(*) n FROM profile_items").get().n,1);
    db.prepare("DELETE FROM profiles WHERE id = ?").run("cloud-id");
    assert.equal(db.prepare("SELECT count(*) n FROM profile_items").get().n,0);
    assert.equal(db.prepare("SELECT name FROM sqlite_master WHERE name='face_embeddings'").get(),undefined);
  } finally { db.close(); }
});

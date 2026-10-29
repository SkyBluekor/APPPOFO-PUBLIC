// Pure configuration preparation. Never invokes Wrangler, creates secrets or touches D1.
import { readFileSync, mkdirSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { PRODUCTION_FACE_HOST } from "../src/face-policy.js";
export function stagingConfig(databaseId, hostname, { enableFaces = false } = {}) {
  if (typeof databaseId === "string") databaseId = databaseId.toLowerCase();
  const production = JSON.parse(readFileSync(new URL("../wrangler.jsonc",import.meta.url),"utf8"));
  if (!/^[a-f0-9]{8}-[a-f0-9]{4}-[1-8][a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/i.test(databaseId) ||
      production.d1_databases.some(d=>d.database_id===databaseId))
    throw new Error("A NEW staging D1 UUID, distinct from production, is required.");
  if (hostname === PRODUCTION_FACE_HOST || !/^[a-z0-9-]+\.[a-z0-9-]+\.workers\.dev$/.test(hostname) ||
      !hostname.startsWith("apppofo-profile-api-staging."))
    throw new Error("The separate staging Worker hostname is required.");
  return {
    name:"apppofo-profile-api-staging",main:"../src/index.js",compatibility_date:production.compatibility_date,
    workers_dev:true,observability:{enabled:false},
    secrets:{required:["APPPOFO_API_TOKEN","APPPOFO_FACE_KEY_V1"]},
    vars:{APPPOFO_DEPLOYMENT_STAGE:"staging",APPPOFO_FACE_REMOTE_ENABLED:enableFaces?"enabled":"disabled",
      APPPOFO_FACE_ALLOWED_HOST:hostname,APPPOFO_FACE_POLICY_VERSION:"staging-face-storage-v1",
      APPPOFO_FACE_SCAN_POLICY_VERSION:"staging-face-scan-v1",
      APPPOFO_FACE_MATCH_THRESHOLD:"0.75",APPPOFO_FACE_MATCH_MARGIN:"0.05"},
    d1_databases:[{binding:"DB",database_name:"apppofo-profiles-staging",database_id:databaseId,migrations_dir:"../migrations"}],
  };
}
if (process.argv[1] && resolve(process.argv[1])===fileURLToPath(import.meta.url)) {
  try {
    const config=stagingConfig(process.argv[2],process.argv[3],{enableFaces:process.argv.includes("--enable-staging-face-apis")});
    const dir=new URL("../.staging/",import.meta.url);mkdirSync(dir,{recursive:true});
    writeFileSync(new URL("wrangler.json",dir),JSON.stringify(config,null,2)+"\n",{flag:"wx"});
    console.log("Prepared .staging/wrangler.json only. No remote action or secret generation performed.");
  } catch(error) { console.error(error.message);process.exitCode=1; }
}

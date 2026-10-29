// Only synthetic vectors and newly-created profiles; no file/secret/real-face input.
import { generateKeyPairSync, createHash, sign, randomUUID } from "node:crypto";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
export async function runStagingSmoke({baseUrl,confirmed=false,fetchFn=fetch,stage=()=>{}}) {
  if (!confirmed || !/^https:\/\/apppofo-profile-api-staging\.[a-z0-9-]+\.workers\.dev$/.test(baseUrl??""))
    throw new Error("Explicit synthetic-only confirmation and a separate staging HTTPS host are required.");
  async function api(path,method="GET",body,token="",expected=200,extra={}) {
    const response=await fetchFn(baseUrl+path,{method,redirect:"error",signal:AbortSignal.timeout(8000),
      headers:{...(token?{Authorization:"Bearer "+token}:{}),...(body?{"Content-Type":"application/json"}:{}),...extra},
      ...(body?{body:JSON.stringify(body)}:{})});
    if(response.status!==expected)throw new Error("Synthetic staging step failed (HTTP "+response.status+").");
    return expected===204?null:response.json();
  }
  const devices=[],profiles=[];
  let success=false,cleanupSucceeded=true;
  async function device() {
    const keys=generateKeyPairSync("ec",{namedCurve:"prime256v1"}),pub=keys.publicKey.export({format:"jwk"});
    const jwk={kty:"EC",crv:"P-256",x:pub.x,y:pub.y};
    const proof=(purpose,id,subject)=>sign("sha256",Buffer.from("AppPofo-v1\n"+purpose+"\n"+id+"\n"+subject),keys.privateKey).toString("base64url");
    const challenge=await api("/api/device/challenge","POST",{purpose:"register",public_key:jwk},"",201);
    const thumb=createHash("sha256").update("AppPofo-P256-v1\n"+jwk.x+"\n"+jwk.y).digest("base64url");
    const registered=await api("/api/device/register","POST",{public_key:jwk,challenge_id:challenge.challenge_id,
      signature:proof("register",challenge.challenge_id,thumb)},"",201);
    const sessionChallenge=await api("/api/device/challenge","POST",{purpose:"session",device_id:registered.device_id},"",201);
    const session=await api("/api/device/session","POST",{device_id:registered.device_id,challenge_id:sessionChallenge.challenge_id,
      signature:proof("session",sessionChallenge.challenge_id,registered.device_id)});
    devices.push(session.access_token);return session.access_token;
  }
  try {
    const owner=await device(),scanner=await device();stage("signed-devices");
    const config=await api("/api/me/face/config","GET",undefined,owner);
    if(config.environment!=="staging"||config.model_version!=="sface_2021dec")throw new Error("Staging configuration mismatch.");
    const key=randomUUID(),draft={display_name:"S510 synthetic smoke",headline:"Synthetic fixture",items:[]};
    const created=await api("/api/me/profiles","POST",draft,owner,201,{"Idempotency-Key":key});
    const id=created.data.id;profiles.push({id,token:owner});
    const replay=await api("/api/me/profiles","POST",draft,owner,201,{"Idempotency-Key":key});
    if(replay.data.id!==id)throw new Error("Creation replay mismatch.");
    const scanning=await api("/api/me/profiles","POST",{display_name:"S510 synthetic scanner",items:[]},scanner,201,{"Idempotency-Key":randomUUID()});
    profiles.push({id:scanning.data.id,token:scanner});stage("idempotent-profiles");
    const path="/api/me/profiles/"+id+"/face";
    const state=(await api(path,"GET",undefined,owner)).data;
    const bytes=Buffer.alloc(1536);for(let p=0;p<3;p++)bytes.writeFloatLE(1,(p*128+p)*4);
    const body={request_id:randomUUID(),expected_revision:state.revision,purge_epoch:state.purge_epoch,
      model_version:"sface_2021dec",embeddings_b64url:bytes.toString("base64url"),
      consent:{policy_version:config.enrollment_policy,storage_opt_in:true,discoverable_opt_in:true,subject_present:true,confirmed_at:Date.now()}};
    const faceHeaders={"If-Face-Consent-Generation":String(state.consent_generation)};
    const ack=(await api(path,"POST",body,owner,201,faceHeaders)).data;
    await api(path,"POST",body,owner,200,faceHeaders);stage("enrollment-replay");
    await api("/api/me/face/scan-consent","POST",{consent:true,policy_version:config.scan_policy},scanner,204);
    const query=Buffer.alloc(512);query.writeFloatLE(1,0);
    const queryBody={request_id:randomUUID(),model_version:"sface_2021dec",embedding_b64url:query.toString("base64url")};
    for(let i=0;i<2;i++) {
      const matched=await api("/api/me/face/match","POST",queryBody,scanner);
      if(!matched.matched||matched.profile.id!==id||matched.profile.display_name!==draft.display_name||
        Object.keys(matched.profile).sort().join(",")!=="avatar_base64,business_card_theme,display_name,headline,id")throw new Error("Public match contract failed.");
    }
    stage("public-match-twice");
    await api(path,"PATCH",{discoverable:false,expected_revision:ack.revision,purge_epoch:state.purge_epoch},owner);
    if((await api("/api/me/face/match","POST",queryBody,scanner)).matched)throw new Error("Withdrawn face matched.");
    await api(path,"DELETE",{request_id:randomUUID(),expected_revision:ack.revision,purge_epoch:state.purge_epoch},owner,204);
    await api("/api/me/face/scan-consent","DELETE",undefined,scanner,204);
    await api("/api/me/face/match","POST",queryBody,scanner,403);stage("withdrawal-and-delete");
    bytes.fill(0);query.fill(0);success=true;
  } finally {
    for(const profile of profiles)try {await api("/api/me/profiles/"+profile.id,"DELETE",undefined,profile.token,204);}catch{cleanupSucceeded=false;}
    for(const token of devices)try {await api("/api/me/logout","POST",undefined,token,204);}catch{cleanupSucceeded=false;}
    stage(cleanupSucceeded?"synthetic-cleanup-confirmed":"synthetic-cleanup-unconfirmed");
  }
  if(!cleanupSucceeded)throw new Error("Synthetic staging cleanup could not be confirmed.");
  return {passed:success,cleaned:true};
}
if(process.argv[1]&&resolve(process.argv[1])===fileURLToPath(import.meta.url)) {
  runStagingSmoke({baseUrl:process.argv[2],confirmed:process.argv.includes("--confirm-staging-synthetic-only"),
    stage:value=>console.log("S5-10 synthetic stage: "+value)})
    .then(()=>console.log("Synthetic staging API smoke PASS. Actual camera E2E remains unverified."))
    .catch(error=>{
      const message=error instanceof Error && /^Synthetic staging step failed \(HTTP \d{3}\)\.$/.test(error.message)
        ? error.message : "Synthetic staging check failed; inspect fixed stage metadata. No credentials or vectors logged.";
      console.error(message);process.exitCode=1;
    });
}

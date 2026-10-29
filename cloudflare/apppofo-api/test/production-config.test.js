import test from 'node:test';
import assert from 'node:assert/strict';
import {remoteFaceEnabled,PRODUCTION_FACE_HOST} from '../src/face-policy.js';
import {matchTestSettings} from '../src/face-match.js';
const settings={APPPOFO_DEPLOYMENT_STAGE:'production',APPPOFO_FACE_ALLOWED_HOST:PRODUCTION_FACE_HOST,
  APPPOFO_FACE_REMOTE_ENABLED:'enabled',APPPOFO_FACE_MATCH_THRESHOLD:'0.65',APPPOFO_FACE_MATCH_MARGIN:'0.05'};
test('production remote faces require the exact production host and explicit deployment flags',()=>{
 const request=new Request('https://'+PRODUCTION_FACE_HOST+'/api/me/face/config');
 assert.equal(remoteFaceEnabled(request,settings),true);
 assert.equal(remoteFaceEnabled(request,{}),false);
 for(const override of [{APPPOFO_DEPLOYMENT_STAGE:'staging'},{APPPOFO_FACE_REMOTE_ENABLED:'disabled'},
  {APPPOFO_FACE_ALLOWED_HOST:'apppofo-profile-api-staging.synthetic.workers.dev'}])
   assert.equal(remoteFaceEnabled(request,{...settings,...override}),false);
 assert.equal(remoteFaceEnabled(new Request('http://'+PRODUCTION_FACE_HOST),settings),false);
 assert.equal(remoteFaceEnabled(new Request('https://apppofo-profile-api-staging.synthetic.workers.dev'),
  {...settings,APPPOFO_FACE_ALLOWED_HOST:'apppofo-profile-api-staging.synthetic.workers.dev'}),false);
 assert.equal(remoteFaceEnabled(request,{APPPOFO_FACE_LOCAL_TEST_MODE:'enabled',APPPOFO_FACE_MATCH_LOCAL_TEST_MODE:'enabled'}),false);
 assert.equal(matchTestSettings(settings,true).decisionVersion,'production-v1');
 assert.throws(()=>matchTestSettings({...settings,APPPOFO_FACE_MATCH_MARGIN:'0.04'},true));
 assert.throws(()=>matchTestSettings({...settings,APPPOFO_FACE_MATCH_THRESHOLD:'0.64'},true));
});

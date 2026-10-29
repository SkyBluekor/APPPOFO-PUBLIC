import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {validAvatarImage} from '../src/profile-avatars.js';

test('display avatar accepts only bounded JPEG dimensions without EXIF or arbitrary media',()=>{
  const jpeg=readFileSync(new URL('./fixtures/avatar.jpg',import.meta.url));
  assert.equal(validAvatarImage(jpeg.toString('base64')),true);
  for(const value of [null,'', 'data:image/jpeg;base64,'+jpeg.toString('base64'),'AAAA','A'.repeat(32772)])
    assert.equal(validAvatarImage(value),false);
  const tooLarge=Buffer.from(jpeg);
  const marker=tooLarge.indexOf(Buffer.from([255,192]));assert.ok(marker>0);
  tooLarge[marker+5]=1;tooLarge[marker+6]=0;
  assert.equal(validAvatarImage(tooLarge.toString('base64')),false);
  const exif=Buffer.concat([jpeg.subarray(0,2),Buffer.from([255,225,0,4,0,0]),jpeg.subarray(2)]);
  assert.equal(validAvatarImage(exif.toString('base64')),false);
});

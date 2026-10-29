"""Regenerate spoken Korean guidance with an external, locally installed eSpeak NG CLI.
The speech engine is a separate GPL-3.0 build tool and is not linked or bundled into Unity.
Generation is deliberately written to a separate output folder for review before replacing assets.
"""
from pathlib import Path
import argparse, subprocess, tempfile, json, hashlib, wave

LINES=[
 ('01_voice_face_front','정면을 바라봐 주세요.'),
 ('02_voice_face_left','고개를 왼쪽으로 천천히 돌려 주세요.'),
 ('03_voice_face_right','고개를 오른쪽으로 천천히 돌려 주세요.'),
 ('04_voice_face_complete','얼굴 등록이 완료되었습니다.'),
 ('05_voice_face_too_dark','주변이 어둡습니다. 밝은 곳으로 이동해 주세요.'),
 ('06_voice_face_center','얼굴을 화면 가운데에 맞춰 주세요.'),
]

def main():
 parser=argparse.ArgumentParser(description=__doc__)
 parser.add_argument('--espeak-exe',required=True,type=Path)
 parser.add_argument('--output-dir',required=True,type=Path)
 args=parser.parse_args()
 exe=args.espeak_exe.resolve(strict=True);output=args.output_dir.resolve()
 root=Path(__file__).resolve().parents[1]
 if output.is_relative_to(root/'Assets'):parser.error('Generate into a separate review folder, then copy only reviewed WAV files while preserving .meta files.')
 version=subprocess.check_output([str(exe),'--version'],text=True,encoding='utf-8').splitlines()[0]
 if not version.startswith('eSpeak NG'):raise SystemExit('Expected eSpeak NG executable')
 output.mkdir(parents=True,exist_ok=True);manifest=[]
 with tempfile.TemporaryDirectory(prefix='apppofo-guidance-') as temporary:
  for name,transcript in LINES:
   text=Path(temporary)/(name+'.txt');text.write_text(transcript,encoding='utf-8')
   target=output/(name+'.wav')
   if target.exists():raise SystemExit('Refusing to overwrite existing output: '+name)
   subprocess.run([str(exe),'-v','ko','-b','1','-s','152','-p','50','-f',str(text),'-w',str(target)],check=True)
   with wave.open(str(target),'rb') as audio:
    if audio.getnchannels()!=1 or audio.getsampwidth()!=2 or audio.getnframes()<audio.getframerate():raise SystemExit('Invalid spoken audio output: '+name)
    duration=audio.getnframes()/audio.getframerate()
   manifest.append({'file':target.name,'transcript':transcript,'duration_seconds':duration,'sha256':hashlib.sha256(target.read_bytes()).hexdigest()})
 (output/'provenance.json').write_text(json.dumps({'engine':version,'voice':'ko','speed':152,'pitch':50,'files':manifest,'note':'Different eSpeak NG releases or random excitation can produce different bytes. This is a reproducible transcript/configuration, not a promise of identical PCM hashes.'},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 print('Generated six spoken Korean clips in the explicit review folder.')

if __name__=='__main__':main()

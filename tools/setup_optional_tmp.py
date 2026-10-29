"""Restore the original TMP default sprite from Unity's version-pinned sample package.
Install-only sample; artwork redistribution rights are separate from the Unity code license.
"""
from pathlib import Path
from urllib.request import urlopen
import argparse,tarfile,io,hashlib,json

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--accept-unity-sample-terms',action='store_true',required=True)
    args=parser.parse_args()
    root=Path(__file__).resolve().parents[1]
    url='https://packages.unity.com/com.unity.textmeshpro/-/com.unity.textmeshpro-3.0.6.tgz'
    blob=urlopen(url,timeout=90).read()
    if hashlib.sha256(blob).hexdigest()!='1ce172027b906a30be33cefe7b2ee46e1c8d35f729359b8b9785fc120d57b637':raise SystemExit('Unity package hash mismatch')
    wanted={
        '381dcb09d5029d14897e55f98031fca5','8f05276190cf498a8153f6cbe761d4e6',
        'c41005c129ba4d66911b75229fd70b45','dffef66376be4fa480fb02b19edbe903',
    }
    installed=[]
    with tarfile.open(fileobj=io.BytesIO(blob),mode='r:gz') as package:
        inner=next(m for m in package.getmembers() if m.name.endswith('TMP Essential Resources.unitypackage'))
        with tarfile.open(fileobj=io.BytesIO(package.extractfile(inner).read()),mode='r:gz') as assets:
            for member in assets.getmembers():
                if not member.name.endswith('/pathname'):continue
                guid=member.name.split('/')[0]
                if guid not in wanted:continue
                relative=assets.extractfile(member).read().decode().strip('\0\r\n')
                target=(root/relative).resolve()
                if not target.is_relative_to(root/'Assets/TextMesh Pro'):raise SystemExit('Unsafe Unity sample path')
                for entry,destination in [('asset',target),('asset.meta',Path(str(target)+'.meta'))]:
                    data=assets.extractfile(guid+'/'+entry).read()
                    if destination.exists() and destination.read_bytes()!=data:raise SystemExit('Refusing to overwrite different asset: '+relative)
                    destination.parent.mkdir(parents=True,exist_ok=True);destination.write_bytes(data)
                installed.append({'path':relative,'guid':guid,'sha256':hashlib.sha256(target.read_bytes()).hexdigest()})
    if len(installed)!=4:raise SystemExit('Incomplete sample restoration')
    p=root/'Audit/Runtime/tmp-sample-install.json';p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps({'source':url,'package_sha256':hashlib.sha256(blob).hexdigest(),'assets':installed,'redistribution':'REVIEW; not included in source publication snapshot'},indent=2)+'\n',encoding='utf-8')
    print('Restored four original Unity TMP sample assets and GUIDs for local validation.')

if __name__=='__main__':main()

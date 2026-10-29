"""Fetch pinned upstream dependencies; verify bytes/SHA-256 and restore original Unity GUIDs.
No Git checkout, original-project access, Cloudflare account or production API is used.
Downloads remain local installation dependencies excluded from the publication snapshot.
"""
from pathlib import Path
from urllib.request import Request, urlopen
from urllib.parse import urlparse
import argparse, hashlib, json, os, shutil, tempfile, time

ALLOWED_HOSTS={'raw.githubusercontent.com','media.githubusercontent.com'}

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--platform',action='append',choices=['Windows','Android','Linux','iOS','macOS'])
    parser.add_argument('--all-platforms',action='store_true')
    parser.add_argument('--accept-upstream-licenses',action='store_true',required=True)
    args=parser.parse_args()
    root=Path(__file__).resolve().parents[1]
    lock=json.loads((root/'tools/dependencies.lock.json').read_text(encoding='utf-8'))
    selected=set(['Windows','Android','Linux','iOS','macOS'] if args.all_platforms else args.platform or ['Windows','Android'])
    results=[]
    for asset in lock['assets']:
        if asset['platform']!='models' and asset['platform'] not in selected:continue
        target=(root/asset['path']).resolve()
        if not target.is_relative_to(root):raise SystemExit('Unsafe destination path')
        def valid(p):return p.is_file() and p.stat().st_size==asset['bytes'] and hashlib.sha256(p.read_bytes()).hexdigest()==asset['sha256']
        if target.exists() and not valid(target):raise SystemExit('Refusing to overwrite mismatched asset: '+asset['path'])
        if not target.exists():
            url=asset['url'];parsed=urlparse(url)
            if parsed.scheme!='https' or parsed.hostname not in ALLOWED_HOSTS:raise SystemExit('Unapproved download origin')
            target.parent.mkdir(parents=True,exist_ok=True)
            for attempt in range(3):
                temp=None
                try:
                    with urlopen(Request(url,headers={'User-Agent':'AppPoFo-public-dependency-setup/1'}),timeout=120) as response:
                        if urlparse(response.url).hostname not in ALLOWED_HOSTS:raise RuntimeError('Unapproved redirect origin')
                        with tempfile.NamedTemporaryFile(dir=target.parent,prefix='.dependency-',suffix='.download',delete=False) as f:
                            temp=Path(f.name);digest=hashlib.sha256();count=0
                            while True:
                                chunk=response.read(1024*1024)
                                if not chunk:break
                                count+=len(chunk)
                                if count>asset['bytes']:raise RuntimeError('Unexpected download size')
                                digest.update(chunk);f.write(chunk)
                    if count!=asset['bytes'] or digest.hexdigest()!=asset['sha256']:raise RuntimeError('Download checksum mismatch')
                    os.replace(temp,target);temp=None;break
                except Exception:
                    if attempt==2:raise
                    time.sleep(1+attempt)
                finally:
                    if temp is not None and temp.exists():temp.unlink()
        meta=Path(str(target)+'.meta')
        template=root/'tools/ExternalAssetTemplates'/(asset['path']+'.meta.template')
        if template.exists():
            if meta.exists() and meta.read_bytes()!=template.read_bytes():raise SystemExit('Existing Unity metadata differs: '+asset['path'])
            shutil.copyfile(template,meta)
        results.append({'path':asset['path'],'sha256':asset['sha256'],'bytes':asset['bytes'],'verified':True,'publication':'local install only'})
        print('Verified '+asset['path'],flush=True)
    # Restore the macOS bundle directory GUID only after its payload is present.
    if 'macOS' in selected:
        relative='Packages/com.opencvplus.unity/Runtime/Plugins/macOS/arm64/OpenCvSharpExtern.bundle.meta'
        template=root/'tools/ExternalAssetTemplates'/(relative+'.template')
        if template.exists():shutil.copyfile(template,root/relative)
    output=root/'Audit/Runtime/dependency-install.json';output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(json.dumps({'assets':results,'source':'Pinned official upstream HTTPS URLs; no original private files copied'},indent=2)+'\n',encoding='utf-8')
    print('Dependency installation verified. Upstream terms and release review remain applicable.')

if __name__=='__main__':main()

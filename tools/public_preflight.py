"""Read-only public snapshot and installed-dependency checks. Exit 2 means owner publication approval is pending; security/hash failures always exit 1."""
from pathlib import Path
import argparse,csv,hashlib,json,re,sys

def main():
 parser=argparse.ArgumentParser(description=__doc__)
 parser.add_argument('--check-installed',action='store_true',help='Require all officially installed payloads and original GUID metadata, in addition to source checks.')
 args=parser.parse_args();root=Path(__file__).resolve().parents[1]
 policy=json.loads((root/'Audit/PUBLICATION_POLICY.json').read_text(encoding='utf-8'))
 excluded=set(policy['excluded_local_install_paths']);prefixes=policy['excluded_generated_prefixes']+policy.get('excluded_local_install_prefixes',[])
 generated=set(policy['excluded_generated_directories']);generated_ext=set(policy['excluded_generated_extensions'])
 def excluded_file(relative):return relative in excluded or any(relative.startswith(p) for p in prefixes) or Path(relative).suffix in generated_ext
 def walk(directory):
  for p in directory.iterdir():
   relative=p.relative_to(root).as_posix()
   if p.is_symlink():
    yield p;continue
   if p.is_dir():
    if p.name not in generated and not any((relative+'/').startswith(x) for x in prefixes):yield from walk(p)
   elif p.is_file() and not excluded_file(relative):yield p
 with (root/'Audit/PUBLIC_FILE_MANIFEST.csv').open(encoding='utf-8-sig',newline='') as f:rows=list(csv.DictReader(f))
 failures=[];expected={r['path'] for r in rows}|{'Audit/PUBLIC_FILE_MANIFEST.csv'}
 for row in rows:
  p=(root/row['path']).resolve()
  if not p.is_relative_to(root) or not p.is_file() or hashlib.sha256(p.read_bytes()).hexdigest()!=row['sha256']:
   failures.append({'path':row['path'],'type':'source_snapshot_missing_or_changed'})
   continue
  b=p.read_bytes()
  if b'\0' not in b[:8192] and re.search(r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY|\bAKIA[A-Z0-9]{16}\b|\b(?:ghp_|github_pat_|sk-proj-)[A-Za-z0-9_]{20,}',b.decode('utf-8-sig',errors='replace')):
   failures.append({'path':row['path'],'type':'possible_secret_signature'})
 for p in walk(root):
  relative=p.relative_to(root).as_posix()
  if p.is_symlink() or relative not in expected:failures.append({'path':relative,'type':'unreviewed_extra_file_or_link'})
 env=json.loads((root/'Assets/AppPofo/Presentation/Resources/AppPofoCloudEnvironment.json').read_text(encoding='utf-8'))
 cfg=json.loads((root/'cloudflare/apppofo-api/wrangler.jsonc').read_text(encoding='utf-8'))
 if env['environment']!='offline' or env.get('production_faces_enabled') or env.get('staging_faces_enabled') or cfg['vars']['APPPOFO_FACE_REMOTE_ENABLED']!='disabled' or cfg['workers_dev']:
  failures.append({'path':'configuration','type':'safe_public_defaults_changed'})
 dependency_failures=[];installed=0;not_installed=[]
 lock=json.loads((root/'tools/dependencies.lock.json').read_text(encoding='utf-8'))
 for asset in lock['assets']:
  p=(root/asset['path']).resolve()
  if not p.is_relative_to(root):dependency_failures.append({'path':asset['path'],'type':'unsafe_lock_path'});continue
  if not p.exists():
   not_installed.append(asset['path'])
   if args.check_installed:dependency_failures.append({'path':asset['path'],'type':'required_local_dependency_missing'})
   continue
  if p.stat().st_size!=asset['bytes'] or hashlib.sha256(p.read_bytes()).hexdigest()!=asset['sha256']:
   dependency_failures.append({'path':asset['path'],'type':'installed_dependency_hash_mismatch'});continue
  installed+=1
  template=root/'tools/ExternalAssetTemplates'/(asset['path']+'.meta.template')
  if template.exists():
   meta=Path(str(p)+'.meta')
   if not meta.exists() or meta.read_bytes()!=template.read_bytes():dependency_failures.append({'path':asset['path']+'.meta','type':'original_model_or_plugin_metadata_mismatch'})
 if args.check_installed:
  for asset in json.loads((root/'Audit/DEPENDENCY_RESTORATION.json').read_text(encoding='utf-8'))['tmp_sample']['assets']:
   p=root/asset['path'];meta=Path(str(p)+'.meta')
   if not p.exists() or hashlib.sha256(p.read_bytes()).hexdigest()!=asset['sha256'] or not meta.exists() or ('guid: '+asset['guid']) not in meta.read_text(encoding='utf-8-sig'):
    dependency_failures.append({'path':asset['path'],'type':'legacy_sample_hash_or_guid_mismatch'})
 summary=json.loads((root/'Audit/VALIDATION_SUMMARY.json').read_text(encoding='utf-8'))
 result={'source_files_checked':len(rows),'source_snapshot_failures':failures,'verified_installed_payloads':installed,'dependencies_not_installed':not_installed,'installed_dependency_failures':dependency_failures,'publication':summary['publication_decision'],'technical_restoration_at_recorded_validation':summary['technical_restoration'],'publication_blockers':summary['publication_blockers'],'publication_review_notes':summary.get('publication_review_notes',[])}
 print(json.dumps(result,ensure_ascii=False,indent=2))
 return 1 if failures or dependency_failures else 2 if summary['publication_decision'] not in {'READY','OWNER_APPROVED_WITH_REVIEW_NOTES'} else 0

if __name__=='__main__':sys.exit(main())

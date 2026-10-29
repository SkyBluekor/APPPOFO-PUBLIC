"""Export the reviewed source manifest to an empty independent directory, without Git or installed payloads."""
from pathlib import Path
import argparse,csv,hashlib,shutil
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);args=p.parse_args()
 root=Path(__file__).resolve().parents[1];destination=args.output.resolve()
 if root==destination or root in destination.parents or destination in root.parents:p.error('Choose an independent destination outside the source project.')
 if destination.exists():p.error('Destination must not exist; existing files are never overwritten.')
 manifest=root/'Audit/PUBLIC_FILE_MANIFEST.csv'
 with manifest.open(encoding='utf-8-sig',newline='') as f:rows=list(csv.DictReader(f))
 plan=[]
 for row in rows:
  source=(root/row['path']).resolve();target=(destination/row['path']).resolve()
  if not source.is_relative_to(root) or not target.is_relative_to(destination) or source.is_symlink():raise SystemExit('Unsafe manifest entry')
  if not source.is_file() or source.stat().st_size!=int(row['bytes']) or hashlib.sha256(source.read_bytes()).hexdigest()!=row['sha256']:raise SystemExit('Reviewed snapshot changed: '+row['path'])
  plan.append((source,target))
 destination.mkdir(parents=True)
 for source,target in plan:target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,target)
 shutil.copyfile(manifest,destination/'Audit/PUBLIC_FILE_MANIFEST.csv')
 print('Exported '+str(len(plan)+1)+' reviewed source files. No Git history, installed models/native payloads, caches or local data copied.')
if __name__=='__main__':main()

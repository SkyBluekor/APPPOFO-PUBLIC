"""Create ignored local Worker secrets. Never reads a production file or prints generated values."""
from pathlib import Path
import argparse,secrets
def main():
 p=argparse.ArgumentParser(description=__doc__);p.parse_args()
 root=Path(__file__).resolve().parents[1];target=root/'cloudflare/apppofo-api/.dev.vars'
 content="""# LOCAL DEVELOPMENT ONLY. Never commit or copy to Unity.
APPPOFO_API_TOKEN={token}
APPPOFO_FACE_KEY_V1={key}
"""
 try:
  with target.open('x',encoding='utf-8',newline='\n') as f:f.write(content.format(token=secrets.token_urlsafe(32),key=secrets.token_urlsafe(32)))
 except FileExistsError:raise SystemExit('Existing local .dev.vars retained; refusing to overwrite.')
 print('Created ignored local .dev.vars with new development-only secrets. Values were not printed. Remote faces remain disabled in wrangler.jsonc.')
if __name__=='__main__':main()

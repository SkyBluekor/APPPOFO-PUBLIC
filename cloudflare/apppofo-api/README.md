# AppPoFo API — local and independently configured example

Workers source, D1 migrations and the existing 85 tests are retained. Unity's default is **offline** and local profile/face/card functionality does not require this server. No original Worker, D1 ID, account, token or profile database is supplied.

## Local-only startup

Use Node.js 22.13.0 or later. From the project root:

```powershell
python tools/create_local_worker_env.py
Set-Location cloudflare/apppofo-api
npm ci --ignore-scripts --no-audit --no-fund
npm run migrate:local
npm run dev
```

Open `http://127.0.0.1:8787/api/health`. The expected response is `{"status":"ok"}`. `dev` and `migrate:local` use local emulation only. The helper writes new independent development secrets to ignored `.dev.vars`, does not print their values and refuses to overwrite an existing file. The encryption key is 32 random bytes in unpadded base64url (43 characters), separate from the administrator token. Remote face processing remains disabled. Do not expose the local development server to the internet.

The checked-in `wrangler.jsonc` is the example configuration: placeholder Worker name, dummy D1 UUID, `workers_dev: false`, remote faces disabled. Never copy production credentials to obtain a working local installation.

## Your own optional Worker

Make a private configuration file from the example, using only a Worker and D1 database that you own. Set its own name, DB binding/ID, environment stage and allowed host. Keep the private configuration outside the public snapshot and pass it explicitly with Wrangler's `--config` option. Generate independent secrets via the secret manager, using the two names `APPPOFO_API_TOKEN` and `APPPOFO_FACE_KEY_V1`. Do not put secrets into Unity Resources, C# constants, Wrangler `vars`, screenshots or a commit. Administrator access and a user's device credentials are different roles; preserve the existing ownership/consent checks.

For Unity, `Assets/AppPofo/Presentation/Resources/AppPofoCloudEnvironment.json` provides the configuration shape. An independently owned HTTPS `*.workers.dev` service can use `environment: "staging"` and `staging_base_url`; leave both face flags false until the service, authentication, policy and explicit consent have been configured and verified. The distributed file remains offline. `ProductionUrl` is a reserved example address, never the original service. This work has not deployed or verified an independently owned remote service.

## Tests and advanced local tools

Run `npm test` here when server code changes; tests use synthetic data and local Miniflare D1. The previously recorded full result is 85/85 PASS. This final packaging task checked a fresh npm install, local migrations, localhost health and the actual AES-GCM functions with a newly generated local key rather than repeating all tests.

The existing `tools/start_face_local.ps1` and related local face tools are retained for explicit, consented development scenarios; read their parameters and generated-file handling before use. Remote smoke/deployment helpers are not part of the local quick start and were not executed.

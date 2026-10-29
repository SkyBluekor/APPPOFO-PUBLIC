# S5-08-B: LOCAL-ONLY Worker + D1. No Cloudflare account writes or deploy.
# Usage: .\tools\start_face_local.ps1 -CloudProfileId "<UUID from selected Unity cloud profile>"
# Run from a terminal; leave this terminal open while using Unity Editor.
param(
    [Parameter(Mandatory=$true)][string]$CloudProfileId,
    [switch]$SettingsOnly,
    [switch]$EnableFaceMatchLocalTest
)
$ErrorActionPreference = 'Stop'
$parsed = [guid]::Empty
if (-not [guid]::TryParse($CloudProfileId, [ref]$parsed)) {
    throw "A valid cloud UUID is required. Never use a local numeric SQLite ID."
}
$profile = $parsed.ToString('D')
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (!(Test-Path -LiteralPath 'package-lock.json')) { throw 'Wrong project folder.' }
    if (!$SettingsOnly -and !(Test-Path -LiteralPath 'node_modules')) {
        npm ci
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
    }

    # All keys created here are synthetic LOCAL TEST secrets. Never commit .dev.vars.
    function New-Secret([int]$size) {
        $bytes = [byte[]]::new($size)
        $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $generator.GetBytes($bytes) }
        finally { $generator.Dispose() }
        return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_')
    }
    # A developer may already have .dev.vars for the older text-profile API.
    # Preserve every existing value; add only missing LOCAL-TEST settings.
    # Never regenerate an existing AES key: doing so could make prior encrypted
    # local records unreadable. Refuse conflicts rather than silently overwrite.
    $varsPath = Join-Path $root '.dev.vars'
    $pairs = @{}
    $existingText = ''
    if (Test-Path -LiteralPath $varsPath) {
        $existingText = [System.IO.File]::ReadAllText($varsPath)
        foreach ($line in [System.IO.File]::ReadAllLines($varsPath)) {
            if ($line -match '^\s*([A-Z][A-Z0-9_]*)\s*=\s*(.*?)\s*$') {
                $key = $Matches[1]
                $value = $Matches[2]
                if ($pairs.ContainsKey($key)) {
                    throw "Duplicate setting in .dev.vars: $key. Resolve it manually; no file changes made."
                }
                if ($value.Length -ge 2 -and
                    (($value.StartsWith('"') -and $value.EndsWith('"')) -or
                     ($value.StartsWith("'") -and $value.EndsWith("'")))) {
                    $value = $value.Substring(1, $value.Length - 2)
                }
                $pairs[$key] = $value
            }
        }
    }

    if ($pairs.ContainsKey('APPPOFO_API_TOKEN') -and
        $pairs['APPPOFO_API_TOKEN'].Length -lt 32) {
        throw 'Existing APPPOFO_API_TOKEN is too short. Nothing was changed.'
    }
    if ($pairs.ContainsKey('APPPOFO_FACE_LOCAL_TEST_MODE') -and
        $pairs['APPPOFO_FACE_LOCAL_TEST_MODE'] -ne 'enabled') {
        throw 'Existing local face mode is not enabled. Nothing was changed.'
    }
    if ($pairs.ContainsKey('APPPOFO_FACE_POLICY_VERSION') -and
        [string]::IsNullOrWhiteSpace($pairs['APPPOFO_FACE_POLICY_VERSION'])) {
        throw 'Existing face policy version is blank. Nothing was changed.'
    }
    if ($pairs.ContainsKey('APPPOFO_FACE_KEY_V1')) {
        $keyText = $pairs['APPPOFO_FACE_KEY_V1']
        if ($keyText -notmatch '^[A-Za-z0-9_-]{43}$') {
            throw 'Existing APPPOFO_FACE_KEY_V1 is not a 32-byte base64url key. Nothing was changed.'
        }
        try {
            $decoded = [Convert]::FromBase64String(
                $keyText.Replace('-', '+').Replace('_', '/') + '=')
            if ($decoded.Length -ne 32) { throw 'Incorrect key length.' }
        } catch {
            throw 'Existing APPPOFO_FACE_KEY_V1 cannot be decoded. Nothing was changed.'
        }
    }

    $missing = New-Object System.Collections.Generic.List[string]
    if (!$pairs.ContainsKey('APPPOFO_API_TOKEN')) {
        $pairs['APPPOFO_API_TOKEN'] = New-Secret 48
        $missing.Add('APPPOFO_API_TOKEN')
    }
    if (!$pairs.ContainsKey('APPPOFO_FACE_KEY_V1')) {
        $pairs['APPPOFO_FACE_KEY_V1'] = New-Secret 32
        $missing.Add('APPPOFO_FACE_KEY_V1')
    }
    if (!$pairs.ContainsKey('APPPOFO_FACE_LOCAL_TEST_MODE')) {
        $pairs['APPPOFO_FACE_LOCAL_TEST_MODE'] = 'enabled'
        $missing.Add('APPPOFO_FACE_LOCAL_TEST_MODE')
    }
    if (!$pairs.ContainsKey('APPPOFO_FACE_POLICY_VERSION')) {
        $pairs['APPPOFO_FACE_POLICY_VERSION'] = 'local-face-test-v1'
        $missing.Add('APPPOFO_FACE_POLICY_VERSION')
    }
    # S5-09-B opt-in: NEVER enable synthetic server matching unless explicitly
    # requested. The device must separately consent through scan-consent API.
    if ($EnableFaceMatchLocalTest) {
        $matchKeys = @{
            APPPOFO_FACE_MATCH_LOCAL_TEST_MODE = 'enabled'
            APPPOFO_FACE_SCAN_POLICY_VERSION = 'local-face-scan-test-v1'
            APPPOFO_FACE_MATCH_THRESHOLD_TEST = '0.75'
            APPPOFO_FACE_MATCH_MARGIN_TEST = '0.05'
        }
        foreach ($name in @('APPPOFO_FACE_MATCH_LOCAL_TEST_MODE',
                            'APPPOFO_FACE_SCAN_POLICY_VERSION',
                            'APPPOFO_FACE_MATCH_THRESHOLD_TEST',
                            'APPPOFO_FACE_MATCH_MARGIN_TEST')) {
            if ($pairs.ContainsKey($name) -and $pairs[$name] -ne $matchKeys[$name]) {
                throw "Existing $name conflicts with local synthetic matching settings. Nothing was overwritten."
            }
            if (!$pairs.ContainsKey($name)) {
                $pairs[$name] = $matchKeys[$name]
                $missing.Add($name)
            }
        }
    }

    if ($missing.Count -gt 0) {
        $separator = if ($existingText.Length -gt 0 -and
            !$existingText.EndsWith("`n")) { "`r`n" } else { '' }
        $linesToAdd = @($missing | ForEach-Object {
            "$_=$($pairs[$_])"
        })
        $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
        [System.IO.File]::AppendAllText($varsPath,
            $separator + ($linesToAdd -join "`r`n") + "`r`n", $utf8NoBom)
        Write-Host ("Added missing LOCAL settings: " + ($missing -join ', '))
        Write-Host 'Existing .dev.vars settings were preserved.'
    } else {
        Write-Host 'All required local settings already exist; no configuration changed.'
    }

    if ($SettingsOnly) {
        Write-Host 'LOCAL SETTINGS CHECK PASS (no Worker, D1 or network operations).'
        return
    }

    # Only SQLite backing store under local .wrangler; never --remote.
    npx wrangler d1 migrations apply DB --local
    if ($LASTEXITCODE -ne 0) { throw 'Local D1 migrations failed.' }
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $sql = "INSERT INTO profiles(id,display_name,created_at,updated_at) VALUES('$profile','로컬 얼굴 테스트',$now,$now) ON CONFLICT(id) DO NOTHING;"
    npx wrangler d1 execute DB --local --command $sql
    if ($LASTEXITCODE -ne 0) { throw 'Could not seed the local-only profile UUID.' }

    Write-Host 'The local administrator token is stored ONLY in the ignored .dev.vars file.'
    Write-Host 'Read APPPOFO_API_TOKEN from that file and enter it in the Unity Editor local-test menu.'
    Write-Host 'Never paste the token in chat, logs, screenshots or public clipboard history.'
    Write-Host "Local profile UUID : $profile"
    Write-Host "Policy version     : $($pairs['APPPOFO_FACE_POLICY_VERSION'])"
    Write-Host 'Unity: Tools > AppPoFo > Local Face Enrollment Test'
    if ($EnableFaceMatchLocalTest) {
        Write-Host 'S5-09 synthetic-only matching: enabled on localhost with separate device scan consent.'
        Write-Host 'Test thresholds are NOT calibrated for real-world identification.'
    }
    Write-Host 'Local Worker: http://127.0.0.1:8787 (Ctrl+C to stop).'
    Write-Host 'The source project may still use a production text-profile UI; only the face test goes to localhost.'
    # wrangler.jsonc lists only the production admin secret. Wrangler loads
    # ONLY secrets.required from .dev.vars, so create a separate ignored
    # localhost config with the three additional face development bindings.
    # The production configuration and remotely deployed Worker are unchanged.
    node .\tools\build_face_local_config.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Local-only Wrangler config generation failed.' }
    npx wrangler dev --config .wrangler.face-local.jsonc --local --ip 127.0.0.1 --port 8787
}
finally {
    # Contains no secret values, but remove the local-only config on shutdown.
    $tempConfig = Join-Path $root '.wrangler.face-local.jsonc'
    if (Test-Path -LiteralPath $tempConfig) {
        Remove-Item -LiteralPath $tempConfig -Force
    }
    Pop-Location
}

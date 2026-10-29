# S5-03D: isolated remote CRUD smoke test. Requires local .env.production.
# Only a uniquely named temporary cloud profile is created and deleted.
# No tokens or profile contents are printed, and no Unity/local SQLite files are touched.
[CmdletBinding()]
param(
    [string]$BaseUrl = "https://portfolio-api.example.invalid"
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd("/")
$apiRoot = Split-Path -Parent $PSScriptRoot
$secretFile = Join-Path $apiRoot ".env.production"
if (-not (Test-Path -LiteralPath $secretFile -PathType Leaf)) {
    throw "Missing local .env.production. Never commit or paste its token."
}

$entry = @(Get-Content -LiteralPath $secretFile | Where-Object {
    $_ -match '^APPPOFO_API_TOKEN='
})
if ($entry.Count -ne 1) {
    throw "Expected exactly one APPPOFO_API_TOKEN line in .env.production."
}
$token = ($entry[0] -split "=", 2)[1].Trim().Trim('"').Trim("'")
if ($token.Length -lt 32) {
    throw "APPPOFO_API_TOKEN is too short."
}
$headers = @{ Authorization = "Bearer $token" }
$createdId = $null
$deleted = $false

function Invoke-Api {
    param([string]$Method, [string]$Path, [object]$Payload = $null)
    $request = @{
        Uri = "$BaseUrl$Path"
        Method = $Method
        Headers = $headers
        UseBasicParsing = $true
        TimeoutSec = 20
        ErrorAction = "Stop"
    }
    if ($null -ne $Payload) {
        $json = ConvertTo-Json -InputObject $Payload -Depth 8 -Compress
        $request.ContentType = "application/json; charset=utf-8"
        $request.Body = [System.Text.Encoding]::UTF8.GetBytes($json)
    }
    return Invoke-WebRequest @request
}

function Assert-HttpError {
    param([scriptblock]$Action, [int]$Status, [string]$Label)
    try {
        & $Action | Out-Null
        throw "$Label expected HTTP $Status but the request succeeded."
    }
    catch {
        $response = $_.Exception.Response
        if ($null -eq $response -or [int]$response.StatusCode -ne $Status) {
            throw
        }
    }
    Write-Host "PASS: $Label (HTTP $Status)"
}

try {
    Assert-HttpError -Status 401 -Label "No token denied" -Action {
        Invoke-WebRequest -Uri "$BaseUrl/api/profiles" -Method Get -UseBasicParsing -TimeoutSec 20 -ErrorAction Stop
    }
    Assert-HttpError -Status 400 -Label "Invalid page rejected" -Action {
        Invoke-Api -Method Get -Path "/api/profiles?limit=25"
    }

    $initialList = Invoke-Api -Method Get -Path "/api/profiles"
    if ([int]$initialList.StatusCode -ne 200) { throw "Initial list was not HTTP 200." }
    $before = $initialList.Content | ConvertFrom-Json
    $initialTotal = [int]$before.total
    Write-Host "PASS: Authenticated list (HTTP 200)"

    $uniqueName = "APPOFO-S5-03D-" + [guid]::NewGuid().ToString("N").Substring(0, 8)
    $createPayload = @{
        display_name = $uniqueName
        headline = "Remote verification only"
        items = @(
            @{ item_type = "PROJECT"; title = "AppPofo"; sort_order = 2 },
            @{ item_type = "LINK"; title = "Documentation"; sort_order = 0 },
            @{ item_type = "SKILL"; title = "Unity"; sort_order = 1 }
        )
    }
    $createResponse = Invoke-Api -Method Post -Path "/api/profiles" -Payload $createPayload
    if ([int]$createResponse.StatusCode -ne 201) { throw "POST was not HTTP 201." }
    $createdId = (($createResponse.Content | ConvertFrom-Json).data.id)
    $guidValue = [guid]::Empty
    if (-not $createdId -or -not [guid]::TryParse($createdId, [ref]$guidValue)) {
        throw "POST did not return a valid cloud UUID."
    }
    Write-Host "PASS: Create cloud-only test profile (HTTP 201)"

    $listed = (Invoke-Api -Method Get -Path "/api/profiles").Content | ConvertFrom-Json
    if ([int]$listed.total -ne ($initialTotal + 1)) {
        throw "List total did not increase by one after POST."
    }
    Write-Host "PASS: List reflects created profile"

    $detailResponse = Invoke-Api -Method Get -Path "/api/profiles/$createdId"
    $detail = ($detailResponse.Content | ConvertFrom-Json).data
    if ([int]$detailResponse.StatusCode -ne 200 -or $detail.id -ne $createdId -or
        $detail.display_name -ne $uniqueName -or @($detail.items).Count -ne 3) {
        throw "Detailed GET failed."
    }
    $orderedTypes = @($detail.items | ForEach-Object { $_.item_type })
    if (($orderedTypes -join ",") -ne "LINK,SKILL,PROJECT") {
        throw "Detailed GET returned wrong item order."
    }
    if ($null -ne $detail.PSObject.Properties["face_embeddings"] -or
        $null -ne $detail.PSObject.Properties["avatar_path"]) {
        throw "Sensitive/local-only fields leaked into cloud detail."
    }
    Write-Host "PASS: Detailed GET and item order (HTTP 200)"

    $updatedName = "$uniqueName-updated"
    $updatePayload = @{
        display_name = $updatedName
        items = @(@{ item_type = "SKILL"; title = "C#"; sort_order = 0 })
    }
    $putResponse = Invoke-Api -Method Put -Path "/api/profiles/$createdId" -Payload $updatePayload
    if ([int]$putResponse.StatusCode -ne 200) { throw "PUT was not HTTP 200." }
    $updated = ((Invoke-Api -Method Get -Path "/api/profiles/$createdId").Content | ConvertFrom-Json).data
    if ($updated.display_name -ne $updatedName -or @($updated.items).Count -ne 1 -or
        $updated.items[0].title -ne "C#") {
        throw "PUT replacement was not persisted."
    }
    Write-Host "PASS: PUT and persisted full replacement (HTTP 200)"

    $deleteResponse = Invoke-Api -Method Delete -Path "/api/profiles/$createdId"
    if ([int]$deleteResponse.StatusCode -ne 204) { throw "DELETE was not HTTP 204." }
    $deleted = $true
    Write-Host "PASS: DELETE test profile (HTTP 204)"

    Assert-HttpError -Status 404 -Label "Deleted profile not found" -Action {
        Invoke-Api -Method Get -Path "/api/profiles/$createdId"
    }
    $finalList = (Invoke-Api -Method Get -Path "/api/profiles").Content | ConvertFrom-Json
    if ([int]$finalList.total -ne $initialTotal) {
        throw "List total did not return to its initial value."
    }
    Write-Host "PASS: Cloud profile count restored"
    Write-Host "S5-03D REMOTE CRUD SMOKE: PASS"
}
finally {
    if ($null -ne $createdId -and -not $deleted) {
        try {
            Invoke-Api -Method Delete -Path "/api/profiles/$createdId" | Out-Null
            Write-Host "CLEANUP: Temporary test profile deleted."
        }
        catch {
            Write-Warning "Cleanup could not be confirmed. Inspect temporary profile ID: $createdId"
        }
    }
    $token = $null
    $headers = $null
}

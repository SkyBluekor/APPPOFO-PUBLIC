# AppPofo S5-01: reproducible Unity regression checks.
# Close any Unity Editor instance that has this project open before using this script.
# Smoke: focused EditMode and PlayMode checks. Full: all tests in each platform.
[CmdletBinding()]
param(
    [ValidateSet('Smoke', 'Full')]
    [string]$Suite = 'Smoke',

    [ValidateSet('Both', 'EditMode', 'PlayMode')]
    [string]$Platform = 'Both',

    [string]$UnityExe = '',

    [string]$OutputDir = ''
)

$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$versionFile = Join-Path $project 'ProjectSettings/ProjectVersion.txt'
if (-not (Test-Path -LiteralPath $versionFile)) {
    throw "ProjectVersion.txt is missing: $versionFile"
}
$versionContents = Get-Content -LiteralPath $versionFile -Raw
$match = [regex]::Match($versionContents, 'm_EditorVersion:\s*(\S+)')
if (-not $match.Success) {
    throw 'Cannot determine the Unity Editor version from ProjectVersion.txt.'
}
$version = $match.Groups[1].Value

if ([string]::IsNullOrWhiteSpace($UnityExe)) {
    $candidates = @(
        (Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor/Unity.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Unity/Hub/Editor/$version/Editor/Unity.exe")
    )
    $found = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ($found) { $UnityExe = $found }
}
if ([string]::IsNullOrWhiteSpace($UnityExe) -or
    -not (Test-Path -LiteralPath $UnityExe -PathType Leaf)) {
    throw "Unity $version executable not found. Pass -UnityExe 'C:\path\to\Unity.exe'."
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutputDir = Join-Path ([IO.Path]::GetTempPath()) "AppPofo-S5-01-$stamp"
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDir).Path

# An Editor already holding this project causes batchmode to exit before running tests.
# Do not terminate other Unity processes automatically.
$targetProjectKey = $project.Replace([char]92, [char]47).TrimEnd('/').ToLowerInvariant()
$runningUnity = @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction SilentlyContinue)
foreach ($unityProcess in $runningUnity) {
    $commandLine = [string]$unityProcess.CommandLine
    if ($commandLine.Replace([char]92, [char]47).ToLowerInvariant().Contains($targetProjectKey)) {
        throw "AppPofo is already open in Unity (PID $($unityProcess.ProcessId)). Close the Editor for this project and rerun. No Unity process was stopped."
    }
}

$smokeFilters = @{
    'EditMode' = @(
        'AppPofo.Tests.EditMode.YuNetExecutionPolicyTests',
        'AppPofo.Tests.EditMode.FaceDetectionControllerTests',
        'AppPofo.Tests.EditMode.FaceRegistrationScreenTests',
        'AppPofo.Tests.EditMode.RearCameraExposureProcessorTests'
    ) -join ';'
    'PlayMode' = @(
        'AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests',
        'AppPofo.Tests.PlayMode.BusinessCardPortraitFlowTests'
    ) -join ';'
}
$platforms = if ($Platform -eq 'Both') { @('EditMode', 'PlayMode') } else { @($Platform) }
$allPassed = $true

Write-Host "Project: $project"
Write-Host "Unity:   $UnityExe"
Write-Host "Suite:   $Suite"
Write-Host "Results: $output"
Write-Host 'Ensure this project is closed in the regular Unity Editor before running.'

foreach ($target in $platforms) {
    $xmlPath = Join-Path $output "$target-results.xml"
    $logPath = Join-Path $output "$target-editor.log"
    Write-Host ""
    Write-Host "=== Running $target ($Suite) ==="
    # Unity.exe is a GUI subsystem application. PowerShell 5.1 can continue without
    # waiting for it when called with '&', so $LASTEXITCODE may be stale.
    # Start-Process -Wait ensures EditMode completes before starting PlayMode.
    $quotedArguments = @(
        '-batchmode',
        ('-projectPath "' + $project + '"'),
        '-runTests',
        ('-testPlatform ' + $target),
        '-testCategory "!Manual"',
        ('-testResults "' + $xmlPath + '"'),
        ('-logFile "' + $logPath + '"')
    )
    if ($Suite -eq 'Smoke') {
        $quotedArguments += ('-testFilter "' + $smokeFilters[$target] + '"')
    }
    $process = Start-Process -FilePath $UnityExe -ArgumentList ($quotedArguments -join ' ') -WindowStyle Hidden -Wait -PassThru
    $processExit = $process.ExitCode

    if (-not (Test-Path -LiteralPath $xmlPath)) {
        Write-Host "FAIL ${target}: no test results XML (Unity exit code $processExit)." -ForegroundColor Red
        if (Test-Path -LiteralPath $logPath) {
            Write-Host 'Last Unity log lines:'
            Get-Content -LiteralPath $logPath -Tail 15 | ForEach-Object { Write-Host $_ }
        }
        Write-Host "See log: $logPath"
        $allPassed = $false
        # Stop here: a startup failure should not trigger another instance.
        break
    }

    try {
        [xml]$document = Get-Content -LiteralPath $xmlPath -Raw
        $run = $document.SelectSingleNode('/test-run')
        if ($null -eq $run) { throw 'Missing NUnit test-run element.' }
        $total = [int]$run.GetAttribute('total')
        $passed = [int]$run.GetAttribute('passed')
        $failed = [int]$run.GetAttribute('failed')
        $result = $run.GetAttribute('result')
        $isSuccess = ($processExit -eq 0 -and $total -gt 0 -and $failed -eq 0 -and
            $passed -gt 0 -and $result -eq 'Passed')

        $status = if ($isSuccess) { 'PASS' } else { 'FAIL' }
        Write-Host "$status $target : total=$total passed=$passed failed=$failed result=$result exit=$processExit"
        if (-not $isSuccess) { $allPassed = $false }
    }
    catch {
        Write-Host "FAIL ${target}: results could not be parsed: $_" -ForegroundColor Red
        $allPassed = $false
    }
}

Write-Host ""
if ($allPassed) {
    Write-Host "S5-01 $Suite regression checks PASS." -ForegroundColor Green
    Write-Host "Logs and XML: $output"
    exit 0
}
Write-Host "S5-01 regression checks FAILED or INCOMPLETE." -ForegroundColor Red
Write-Host "Logs and XML: $output"
exit 1

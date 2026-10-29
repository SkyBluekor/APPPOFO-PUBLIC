# Synthetic ephemeral CNG signature check. No persisted AppPoFo key is opened/created.
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'This contract check requires Windows.'
}
$projectPath = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testRoot = Join-Path $projectPath '.utmp/s5-10/windows-key-contract'
New-Item -ItemType Directory -Force $testRoot | Out-Null
$major = [int]((& dotnet --version).Split('.')[0])
if ($major -lt 8) { throw 'Install .NET SDK 8 or later for this standalone check.' }
$keySource = [Security.SecurityElement]::Escape((Join-Path $projectPath 'Assets/AppPofo/Presentation/Common/WindowsDeviceKey.cs'))
$testSource = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'WindowsDeviceKeyContract.cs'))
@"
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net$major.0</TargetFramework><DefineConstants>UNITY_EDITOR_WIN</DefineConstants><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
<ItemGroup><Compile Include="$keySource" /><Compile Include="$testSource" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $testRoot 'contract.csproj') -Encoding utf8
$previousCliHome = $env:DOTNET_CLI_HOME
try {
    $env:DOTNET_CLI_HOME = Join-Path $testRoot 'dotnet'
    & dotnet run --project (Join-Path $testRoot 'contract.csproj') -p:RestoreIgnoreFailedSources=true --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Windows CNG contract check failed.' }
}
finally { $env:DOTNET_CLI_HOME = $previousCliHome }

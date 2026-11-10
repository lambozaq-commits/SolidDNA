param(
    [string]$Destination = 'C:\ProgramData\CabinTools'
)

$ErrorActionPreference = 'Stop'

if (Get-Process -Name 'SLDWORKS' -ErrorAction SilentlyContinue) {
    Write-Host 'SOLIDWORKS is running. Close SOLIDWORKS before deploying Cabin Tools.' -ForegroundColor Yellow
    exit 2
}

$projectRoot = $PSScriptRoot
$sourceDir = Join-Path $projectRoot 'bin\Release'
$sourceDll = Join-Path $sourceDir 'SolidDNA.dll'
if (-not (Test-Path -LiteralPath $sourceDll)) {
    throw "Release DLL was not found: $sourceDll. Rebuild the Release configuration first."
}
$dll = Get-Item -LiteralPath $sourceDll
Write-Host "Using build output: $sourceDir" -ForegroundColor Cyan
Write-Host "Deploying to: $Destination" -ForegroundColor Cyan

if (-not (Test-Path $Destination)) {
    New-Item -Path $Destination -ItemType Directory -Force | Out-Null
}

$backupRoot = Join-Path $Destination 'Backup'
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupDir = Join-Path $backupRoot $stamp
New-Item -Path $backupDir -ItemType Directory -Force | Out-Null

$existingDll = Join-Path $Destination 'SolidDNA.dll'
if (Test-Path $existingDll) {
    Copy-Item $existingDll -Destination (Join-Path $backupDir 'SolidDNA.dll') -Force
}

Get-ChildItem -Path $sourceDir -Force | ForEach-Object {
    Copy-Item $_.FullName -Destination $Destination -Recurse -Force
}

$deployedDll = Join-Path $Destination 'SolidDNA.dll'
if (-not (Test-Path $deployedDll)) {
    throw "Deployment failed: $deployedDll was not created."
}
$sourceHash = (Get-FileHash -LiteralPath $sourceDll -Algorithm SHA256).Hash
$deployedHash = (Get-FileHash -LiteralPath $deployedDll -Algorithm SHA256).Hash
if ($sourceHash -ne $deployedHash) {
    throw "Deployment failed: the Release DLL does not match $deployedDll."
}

$info = @(
    'Cabin Tools Release build',
    "Deployed: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    "Source: $($dll.FullName)",
    "Destination: $deployedDll",
    "DLL timestamp: $((Get-Item $deployedDll).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))",
    "SHA256: $deployedHash"
)
$info | Set-Content -Path (Join-Path $Destination 'DEPLOYED_BUILD.txt') -Encoding UTF8

Write-Host ''
Write-Host 'Cabin Tools deployment completed.' -ForegroundColor Green
Write-Host "Deployed DLL: $deployedDll"
Write-Host "SHA256 matches Release build: $deployedHash"
Write-Host 'Start SOLIDWORKS and verify the Cabin Tools commands load from C:\ProgramData\CabinTools.'

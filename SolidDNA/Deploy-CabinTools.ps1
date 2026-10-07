param(
    [string]$Destination = 'C:\ProgramData\CabinTools'
)

$ErrorActionPreference = 'Stop'

if (Get-Process -Name 'SLDWORKS' -ErrorAction SilentlyContinue) {
    Write-Host 'SOLIDWORKS is running. Close SOLIDWORKS before deploying Cabin Tools.' -ForegroundColor Yellow
    exit 2
}

$projectRoot = $PSScriptRoot
$releaseDll = Join-Path $projectRoot 'SolidDNA.dll'
if (Test-Path -LiteralPath $releaseDll) {
    $dll = Get-Item -LiteralPath $releaseDll
}
else {
    $binRoot = Join-Path $projectRoot 'bin'
    if (-not (Test-Path -LiteralPath $binRoot)) {
        throw "Neither a release DLL nor a project build-output folder was found under: $projectRoot"
    }

    $dll = Get-ChildItem -LiteralPath $binRoot -Filter 'SolidDNA.dll' -File -Recurse |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

if (-not $dll) {
    throw 'SolidDNA.dll was not found under the project bin folder. Build the project first.'
}

$sourceDir = $dll.Directory.FullName
Write-Host "Using build output: $sourceDir" -ForegroundColor Cyan
Write-Host "Deploying to: $Destination" -ForegroundColor Cyan

if (-not (Test-Path $Destination)) {
    New-Item -Path $Destination -ItemType Directory -Force | Out-Null
}

$backupRoot = Join-Path $Destination 'Backup'
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupDir = Join-Path $backupRoot $stamp
New-Item -Path $backupDir -ItemType Directory -Force | Out-Null

$runtimeFiles = Get-ChildItem -LiteralPath $sourceDir -File | Where-Object {
    $_.Extension -in @('.dll', '.pdb', '.xml', '.config')
}

foreach ($runtimeFile in $runtimeFiles) {
    $existingFile = Join-Path $Destination $runtimeFile.Name
    if (Test-Path -LiteralPath $existingFile) {
        Copy-Item -LiteralPath $existingFile -Destination (Join-Path $backupDir $runtimeFile.Name) -Force
    }
    Copy-Item -LiteralPath $runtimeFile.FullName -Destination $Destination -Force
}

$sourceResources = Join-Path $sourceDir 'Resources'
if (Test-Path -LiteralPath $sourceResources) {
    Copy-Item -LiteralPath $sourceResources -Destination $Destination -Recurse -Force
}

$deployedDll = Join-Path $Destination 'SolidDNA.dll'
if (-not (Test-Path $deployedDll)) {
    throw "Deployment failed: $deployedDll was not created."
}

$info = @(
    'Cabin Tools v3.11.1',
    "Deployed: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    "Source: $($dll.FullName)",
    "Destination: $deployedDll",
    "DLL timestamp: $((Get-Item $deployedDll).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))"
)
$info | Set-Content -Path (Join-Path $Destination 'DEPLOYED_BUILD.txt') -Encoding UTF8

Write-Host ''
Write-Host 'Cabin Tools deployment completed.' -ForegroundColor Green
Write-Host "Deployed DLL: $deployedDll"
Write-Host 'Start SOLIDWORKS and verify the Cabin Tools commands load from C:\ProgramData\CabinTools.'

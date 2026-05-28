param(
    [ValidateSet("Dev", "Plugin")]
    [string] $Target = "Dev"
)

$ErrorActionPreference = "Stop"

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$gameDir = Resolve-Path (Join-Path $projectDir "..\..")
$outputDir = if ($Target -eq "Plugin") {
    Join-Path $gameDir "BepInEx\plugins\AutoMap"
} else {
    Join-Path $gameDir "BepInEx\scripts"
}
$dependencyDir = Join-Path $gameDir "BepInEx\plugins\SaveScopedConfig"

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
New-Item -ItemType Directory -Force -Path $dependencyDir | Out-Null

dotnet build "$projectDir\..\SaveScopedConfig\SaveScopedConfig.csproj" `
    --configuration Release `
    --nologo `
    -p:OutDir="$dependencyDir\"

if ($LASTEXITCODE -ne 0) {
    throw "SaveScopedConfig build failed with exit code $LASTEXITCODE"
}

dotnet build "$projectDir\AutoMap.csproj" `
    --configuration Release `
    --nologo `
    -p:OutDir="$outputDir\"

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

$depsFile = Join-Path $outputDir "AutoMap.deps.json"
if (Test-Path -LiteralPath $depsFile) {
    Remove-Item -LiteralPath $depsFile -Force
}

$saveScopedDepsFile = Join-Path $outputDir "SaveScopedConfig.deps.json"
if (Test-Path -LiteralPath $saveScopedDepsFile) {
    Remove-Item -LiteralPath $saveScopedDepsFile -Force
}

$dependencyDepsFile = Join-Path $dependencyDir "SaveScopedConfig.deps.json"
if (Test-Path -LiteralPath $dependencyDepsFile) {
    Remove-Item -LiteralPath $dependencyDepsFile -Force
}

$scriptSaveScopedDll = Join-Path $outputDir "SaveScopedConfig.dll"
if (Test-Path -LiteralPath $scriptSaveScopedDll) {
    Remove-Item -LiteralPath $scriptSaveScopedDll -Force
}

$scriptSaveScopedPdb = Join-Path $outputDir "SaveScopedConfig.pdb"
if (Test-Path -LiteralPath $scriptSaveScopedPdb) {
    Remove-Item -LiteralPath $scriptSaveScopedPdb -Force
}

Write-Host "Built $(Join-Path $dependencyDir 'SaveScopedConfig.dll')"
Write-Host "Built $(Join-Path $outputDir 'AutoMap.dll')"

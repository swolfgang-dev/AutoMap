param(
    [ValidateSet("Build", "Dev", "Plugin")][string] $Target = "Build",
    [string] $GameDir = $env:SILKSONG_GAME_DIR,
    [string] $SaveScopedConfigPath = ""
)
$ErrorActionPreference = "Stop"
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $GameDir) { throw "Pass -GameDir or set SILKSONG_GAME_DIR." }
$gamePath = (Resolve-Path -LiteralPath $GameDir).Path
$project = Get-ChildItem -LiteralPath $projectDir -Filter *.csproj | Select-Object -First 1
$arguments = @("build", $project.FullName, "-c", "Release", "--nologo", "-p:GameDir=$gamePath")
if ($SaveScopedConfigPath) { $arguments += "-p:SaveScopedConfigPath=$SaveScopedConfigPath" }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
$dll = Join-Path $projectDir "bin/Release/netstandard2.1/$($project.BaseName).dll"
if ($Target -ne "Build") {
    $relative = if ($Target -eq "Plugin") { "BepInEx/plugins/$($project.BaseName)" } else { "BepInEx/scripts" }
    $destination = Join-Path $gamePath $relative
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item -LiteralPath $dll -Destination $destination
}
Write-Host "Built $dll"

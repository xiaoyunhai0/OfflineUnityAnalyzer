param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$publishRoot = Join-Path $root "publish/OfflineUnityAnalyzer-$Runtime"

if (Test-Path $publishRoot) {
    Remove-Item -Recurse -Force $publishRoot
}

dotnet publish (Join-Path $root "src/OfflineUnityAnalyzer.App/OfflineUnityAnalyzer.App.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -o $publishRoot

dotnet publish (Join-Path $root "src/OfflineUnityAnalyzer.Cli/OfflineUnityAnalyzer.Cli.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -o $publishRoot

Copy-Item -Recurse -Force (Join-Path $root "assets") (Join-Path $publishRoot "assets")
Copy-Item -Recurse -Force (Join-Path $root "templates") (Join-Path $publishRoot "templates")
Copy-Item -Recurse -Force (Join-Path $root "docs") (Join-Path $publishRoot "docs")
Copy-Item -Force (Join-Path $root "samples/config.sample.json") (Join-Path $publishRoot "config.sample.json")
Copy-Item -Force (Join-Path $root "README.md") (Join-Path $publishRoot "README.md")
Copy-Item -Force (Join-Path $root "README.zh-CN.md") (Join-Path $publishRoot "README.zh-CN.md")

$zipPath = "$publishRoot.zip"
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

Compress-Archive -Path $publishRoot -DestinationPath $zipPath -Force

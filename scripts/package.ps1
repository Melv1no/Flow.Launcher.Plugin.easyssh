<#
.SYNOPSIS
    Builds the plugin and creates the zip published on GitHub releases (artifacts/easyssh.zip).

.DESCRIPTION
    The zip contains the plugin files at its root. Its name must stay "easyssh.zip": the Flow Launcher
    plugin manifest points to this asset, and its updater picks new releases up automatically.
#>
param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\artifacts")
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "Flow.Launcher.Plugin.easyssh\Flow.Launcher.Plugin.easyssh.csproj"
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$OutputDirectory = Resolve-Path $OutputDirectory
$publishDirectory = Join-Path $OutputDirectory "publish"
$zip = Join-Path $OutputDirectory "easyssh.zip"

if (Test-Path $publishDirectory) { Remove-Item $publishDirectory -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }

dotnet publish $project --configuration $Configuration --output $publishDirectory --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$pluginJson = Get-Content (Join-Path $publishDirectory "plugin.json") -Raw | ConvertFrom-Json
foreach ($required in @($pluginJson.ExecuteFileName, "plugin.json", "Images\app.png", "Languages\en.xaml")) {
    if (-not (Test-Path (Join-Path $publishDirectory $required))) { throw "Missing file in the package: $required" }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($publishDirectory, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host "EasySSH $($pluginJson.Version) packaged: $zip"

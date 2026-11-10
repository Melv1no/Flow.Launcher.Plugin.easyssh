<#
.SYNOPSIS
    Builds the plugin and installs it in the local Flow Launcher, for manual testing.

.DESCRIPTION
    Stops Flow Launcher, moves the installed EasySSH folder to a backup folder (it is never deleted),
    copies the fresh build and starts Flow Launcher again.
    Your profiles are not touched: they live in Flow Launcher's settings folder.

.PARAMETER PluginsDirectory
    Flow Launcher's plugins folder. Change it for a portable installation.
#>
param(
    [string]$Configuration = "Debug",
    [string]$PluginsDirectory = (Join-Path $env:APPDATA "FlowLauncher\Plugins"),
    [string]$FlowLauncher = (Join-Path $env:LOCALAPPDATA "FlowLauncher\Flow.Launcher.exe")
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "Flow.Launcher.Plugin.easyssh\Flow.Launcher.Plugin.easyssh.csproj"
$pluginId = (Get-Content (Join-Path $root "Flow.Launcher.Plugin.easyssh\plugin.json") -Raw | ConvertFrom-Json).ID
$build = Join-Path ([System.IO.Path]::GetTempPath()) "easyssh-build"

if (Test-Path $build) { Remove-Item $build -Recurse -Force }
dotnet publish $project --configuration $Configuration --output $build --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Get-Process -Name "Flow.Launcher" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

$backup = Join-Path ([System.IO.Path]::GetTempPath()) ("easyssh-backup-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Get-ChildItem $PluginsDirectory -Directory | ForEach-Object {
    $manifest = Join-Path $_.FullName "plugin.json"
    if ((Test-Path $manifest) -and ((Get-Content $manifest -Raw | ConvertFrom-Json).ID -eq $pluginId)) {
        New-Item -ItemType Directory -Force -Path $backup | Out-Null
        Move-Item $_.FullName $backup
        Write-Host "Previous installation moved to $backup\$($_.Name)"
    }
}

Copy-Item $build (Join-Path $PluginsDirectory "EasySSH-dev") -Recurse
Write-Host "Installed in $(Join-Path $PluginsDirectory 'EasySSH-dev')"

if (Test-Path $FlowLauncher) { Start-Process $FlowLauncher }

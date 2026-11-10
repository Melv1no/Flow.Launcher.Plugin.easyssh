<#
.SYNOPSIS
    Packages the plugin and installs it in the local Flow Launcher, to test it as users will get it.

.DESCRIPTION
    Creates artifacts/easyssh.zip with package.ps1 (the zip published on GitHub releases), closes Flow Launcher,
    moves the installed EasySSH (any version) to a backup folder, unzips the package in the plugins folder as the
    plugin store does, and starts Flow Launcher again.

    Nothing is deleted: previous installations are kept in "EasySSH-backups", next to the plugins folder.
    To go back, exit Flow Launcher, delete the new EasySSH folder and move the backup into the plugins folder.
    The data of EasySSH 3 is in Flow Launcher's settings folder; %USERPROFILE%\.ssh\profiles.json (EasySSH 1.x)
    is only read.

.PARAMETER Configuration
    Release (default, as published) or Debug.

.PARAMETER PluginsDirectory
    Flow Launcher's plugins folder. Change it for a portable installation.

.PARAMETER FlowLauncher
    Flow Launcher's program, started again at the end.

.PARAMETER ResetData
    Also moves the data of EasySSH 3 to the backup folder: the next start is a first start, and the data of
    EasySSH 1.x is imported again.
#>
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [string]$PluginsDirectory = (Join-Path $env:APPDATA "FlowLauncher\Plugins"),
    [string]$FlowLauncher = (Join-Path $env:LOCALAPPDATA "FlowLauncher\Flow.Launcher.exe"),
    [switch]$ResetData
)

$ErrorActionPreference = "Stop"

function Close-FlowLauncher {
    $processes = @(Get-Process -Name "Flow.Launcher" -ErrorAction SilentlyContinue)
    if ($processes.Count -eq 0) { return }

    # Closing the main window is what "Exit" does: Flow Launcher saves its settings and disposes the plugins.
    # Stopping the process would lose what it hasn't saved yet (usage of the results, history).
    # The window is hidden, so Process.CloseMainWindow() can't find it.
    if (-not ("EasySshDeploy.Windows" -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace EasySshDeploy
{
    public static class Windows
    {
        private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        private const uint WM_CLOSE = 0x0010;

        // Posts WM_CLOSE to the WPF windows of the process that have one of the titles, hidden or not.
        public static bool PostClose(int processId, string[] titles)
        {
            bool posted = false;
            EnumWindows(delegate (IntPtr window, IntPtr parameter)
            {
                uint owner;
                GetWindowThreadProcessId(window, out owner);
                if (owner != (uint)processId) return true;

                StringBuilder title = new StringBuilder(256);
                StringBuilder className = new StringBuilder(256);
                GetWindowText(window, title, title.Capacity);
                GetClassName(window, className, className.Capacity);
                if (className.ToString().StartsWith("HwndWrapper[") && Array.IndexOf(titles, title.ToString()) >= 0)
                {
                    posted |= PostMessage(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
                return true;
            }, IntPtr.Zero);
            return posted;
        }
    }
}
'@
    }

    foreach ($process in $processes) {
        $titles = [string[]]@("Flow.Launcher", "Flow Launcher")
        if ([EasySshDeploy.Windows]::PostClose($process.Id, $titles) -and $process.WaitForExit(30000)) {
            Write-Host "Flow Launcher closed."
            continue
        }
        Write-Warning "Flow Launcher didn't exit by itself: stopping it."
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$artifacts = Join-Path $root "artifacts"

# Package first: if the build fails, Flow Launcher is left alone.
& (Join-Path $PSScriptRoot "package.ps1") -Configuration $Configuration -OutputDirectory $artifacts
$zip = Join-Path $artifacts "easyssh.zip"
$manifest = Get-Content (Join-Path $artifacts "publish\plugin.json") -Raw | ConvertFrom-Json
$target = Join-Path $PluginsDirectory "$($manifest.Name)-$($manifest.Version)"
$flowData = Split-Path $PluginsDirectory -Parent
# Flow Launcher gives each plugin a settings folder named after its assembly.
$dataDirectory = Join-Path $flowData ("Settings\Plugins\" + [System.IO.Path]::GetFileNameWithoutExtension($manifest.ExecuteFileName))
# Next to the plugins folder: on the same drive (moving is instant) and not cleaned like the temp folder.
$backup = Join-Path $flowData ("EasySSH-backups\" + (Get-Date -Format "yyyyMMdd-HHmmss"))

Close-FlowLauncher

# Every installed copy of the plugin, found by its ID whatever the folder name.
$installed = @(Get-ChildItem $PluginsDirectory -Directory | Where-Object {
    $file = Join-Path $_.FullName "plugin.json"
    ($_.FullName -eq $target) -or ((Test-Path $file) -and ((Get-Content $file -Raw | ConvertFrom-Json).ID -eq $manifest.ID))
})
foreach ($folder in $installed) {
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    Move-Item $folder.FullName $backup
    Write-Host "Previous installation moved to $(Join-Path $backup $folder.Name)"
}

if ($ResetData -and (Test-Path $dataDirectory)) {
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    Move-Item $dataDirectory $backup
    Write-Host "Data moved to $(Join-Path $backup (Split-Path $dataDirectory -Leaf))"
}

Expand-Archive -Path $zip -DestinationPath $target
Write-Host "EasySSH $($manifest.Version) ($Configuration) installed in $target"

# Started by Explorer, as from the Start menu: not elevated, and independent of this console.
if (Test-Path $FlowLauncher) {
    Start-Process explorer.exe -ArgumentList "`"$FlowLauncher`""
    Write-Host "Flow Launcher started."
}

<img src="Flow.Launcher.Plugin.easyssh/Images/app.png" alt="EasySSH" width="96" align="right">

EasySSH for Flow Launcher
=========================

[![Latest release](https://img.shields.io/github/v/release/Melv1no/Flow.Launcher.Plugin.easyssh)](https://github.com/Melv1no/Flow.Launcher.Plugin.easyssh/releases/latest)
[![CI](https://github.com/Melv1no/Flow.Launcher.Plugin.easyssh/actions/workflows/ci.yml/badge.svg)](https://github.com/Melv1no/Flow.Launcher.Plugin.easyssh/actions/workflows/ci.yml)

A [Flow Launcher](https://www.flowlauncher.com/) plugin to save, search and open SSH connections in your favorite terminal.

![EasySSH](https://github.com/Melv1no/Flow.Launcher.Plugin.easyssh/assets/66535418/2b864355-51c6-4a9a-b4ad-9460cf9328d3)

- Type `ssh` and a few letters to find a connection, press <kbd>Enter</kbd> to open it.
- Hosts of your `~/.ssh/config` are listed automatically, next to your saved profiles.
- Search ignores case and accents, and tolerates typos.
- Open connections in Command Prompt, Windows Terminal, PowerShell, or any terminal you configure.
- Manage everything from the search bar or from the plugin settings.

## Requirements

- Flow Launcher **2.0** or later.
- The OpenSSH client (`ssh.exe`). It ships with Windows 10/11; if it's missing, add **OpenSSH Client** in
  *Settings → System → Optional features*. Other programs (PuTTY's `plink`, `wsl`, `mosh`…) work too.

## Installation

Type `pm install EasySSH` in Flow Launcher, or download `easyssh.zip` from the
[latest release](https://github.com/Melv1no/Flow.Launcher.Plugin.easyssh/releases/latest) and unzip it into
`%APPDATA%\FlowLauncher\Plugins\`.

## Quick start

```
ssh add prod admin@203.0.113.10 -p 2222     save a profile
ssh prod                                    find it, press Enter to connect
```

## Commands

| Command | What it does |
|---|---|
| `ssh <search>` | Search profiles and `~/.ssh/config` hosts. The last result connects to what you typed, without saving it. |
| `ssh add <name> <destination or command>` | Save a profile, or update the one with that name. Use quotes for names with spaces: `ssh add "My server" …` |
| `ssh edit <search>` | Put a profile back in the search bar to change its command. |
| `ssh remove <search>` | Remove a profile (a confirmation is asked). Alias: `rm`. |
| `ssh profiles <search>` | Search only (no direct connection). Alias: `p`. |
| `ssh d <destination or command>` | Connect without saving. Alias: `direct`. |
| `ssh terminal` | Choose the terminal used to open connections. Alias: `shell`. |
| `ssh terminal add <name> <program> [arguments]` | Add a custom terminal (see below). `ssh terminal add <program>` also works. |
| `ssh terminal remove` | Remove a custom terminal. |
| `ssh config <search>` | List the hosts of `~/.ssh/config`. |
| `ssh help` | List the commands. Alias: `docs`. |

Press <kbd>Tab</kbd> to complete a command or a profile name, and <kbd>Shift</kbd>+<kbd>Enter</kbd> (or right click)
on a result to copy its command, edit or remove it, or open it in another terminal. <kbd>Ctrl</kbd>+<kbd>C</kbd> copies
the command of the selected result.

### How a command is run

A command that starts with a program is run as-is: `ssh …`, `scp …`, `wsl ssh …`, `plink -ssh …`,
`C:\tools\something.exe …`. Anything else is given to ssh:

| You type | EasySSH runs |
|---|---|
| `admin@10.0.0.5` | `ssh admin@10.0.0.5` |
| `admin@10.0.0.5 -p 2222` | `ssh admin@10.0.0.5 -p 2222` |
| `bastion` (a host of `~/.ssh/config`) | `ssh bastion` |
| `ssh -i "C:\keys\id_ed25519" admin@web` | unchanged |

To run a program EasySSH doesn't know, write its extension (`kubectl.exe exec …`) or its full path.
If one of your hosts is named like a program (`docker`, `wsl`…), write `ssh docker`.

**Quotes:** on Windows, only double quotes group words. A path in single quotes such as `-i 'C:\keys\id'` is converted
to double quotes automatically, but prefer `-i "C:\keys\id"`.

## Search

The search is case-insensitive and accent-insensitive: `preprod` matches `Préprod-web`. Several terms can be typed;
each must match the profile name or its command.

### Ranking

Results are ranked by relevance:

1. Exact match on both name and command
2. Exact match on name only
3. Exact match on command only
4. Fuzzy match on both name and command
5. Fuzzy match on name only
6. Any other match (fuzzy on the command only, or terms split between name and command)

Within a tier come first the names starting with what you typed, then saved profiles before `~/.ssh/config`
hosts, then the connections you use the most, then the alphabetical order.

### Typo tolerance

The search uses the Damerau-Levenshtein distance and tolerates 1 error per 5 characters, including:

- Substitutions (`servrr` → `server`)
- Insertions (`serrver` → `server`)
- Deletions (`servr` → `server`)
- Transpositions (`servre` → `server`)

## Terminals

| Terminal | How the command is run |
|---|---|
| **Command Prompt** (default) | `cmd.exe`, shown in the terminal chosen in *Windows Settings → For developers → Terminal*. |
| **Windows Terminal** | A new tab in the last Windows Terminal window, titled with the profile name. |
| **Windows PowerShell** / **PowerShell 7** | The command is run by PowerShell. |

With Command Prompt and PowerShell, the window stays open when ssh fails, so you can read the error.
Uncheck *Keep the window open when the connection fails* in the settings to close it immediately.
Windows Terminal keeps a failed tab open by itself.

### Custom terminals

Add them in the settings or with `ssh terminal add <name> <program> [arguments]`. In the arguments, `{command}` is
replaced by the command to run and `{name}` by the profile name. Without `{command}`, the command is added at the end.

| Example | Program | Arguments |
|---|---|---|
| Windows Terminal, "Git Bash" profile | `wt.exe` | `-w 0 new-tab -p "Git Bash" {command}` |
| Alacritty | `alacritty.exe` | `-e {command}` |
| WezTerm | `wezterm.exe` | `start -- {command}` |

## Hosts of `~/.ssh/config`

Every `Host` of your OpenSSH configuration that names a single host (no `*`, `?` or `!`) is listed with your
profiles, with its `User`, `HostName` and `Port`. `Include` directives are followed. The file is read again
when it changes. Use *Save as a profile* in the context menu to copy a host into your profiles, or uncheck
*Show the hosts of ~/.ssh/config* in the settings to hide them.

## Settings

*Flow Launcher → Settings → Plugins → EasySSH* lets you choose the terminal, manage custom terminals and
profiles (including renaming), show or hide `~/.ssh/config` hosts, and open the data folder.

## Data

Profiles and settings are stored in `easyssh.json`, in Flow Launcher's plugin settings folder
(`%APPDATA%\FlowLauncher\Settings\Plugins\Flow.Launcher.Plugin.easyssh\`, or the portable data folder).
Each save is atomic and keeps the previous version as `easyssh.json.bak`. A damaged file is never overwritten:
it is renamed `easyssh.json.damaged-<date>` and the backup is used.

### Upgrading from EasySSH 1.x

On its first start, EasySSH 3 imports the profiles and custom shells of `%USERPROFILE%\.ssh\profiles.json`,
whichever version wrote it. Commands saved without `ssh` (`root@host`) are fixed on the way. The old file is
left untouched; the import can be run again from the settings.

## Troubleshooting

- **Nothing happens / the window closes immediately:** check the command with `ssh d <your command>`, and keep
  *Keep the window open when the connection fails* checked to read the error.
- **"OpenSSH client not found":** add *OpenSSH Client* in Windows optional features, or use a full command
  with another program.
- **The plugin doesn't load:** look at Flow Launcher's log (type `sys`, then *Open Log Location*) and open an
  [issue](https://github.com/Melv1no/Flow.Launcher.Plugin.easyssh/issues) with the lines mentioning EasySSH.

## Development

Requirements: the .NET 9 SDK (or later) on Windows.

```powershell
dotnet build                     # build the plugin and the tests
dotnet test                      # run the tests
./scripts/deploy-local.ps1       # build and install in your Flow Launcher (the installed copy is moved to a backup)
./scripts/package.ps1            # create artifacts/easyssh.zip, as published
```

The icons of `Flow.Launcher.Plugin.easyssh/Images` are drawn by `scripts/icons/generate-icons.cs` (colors, shapes
and badges are at the top of the file). To change them, edit the script and run it with the .NET 10 SDK:

```powershell
dotnet run scripts/icons/generate-icons.cs                              # write the PNG files
dotnet run scripts/icons/generate-icons.cs -- --preview preview.png     # also write a sheet of every size
```

The plugin is compiled against `Flow.Launcher.Plugin` 5.0.0, the API of Flow Launcher 2.0 (`MinimumAppVersion`
in `plugin.json`), so it can't use a newer API by mistake. The code is split in:

- `Core/`: pure logic (command parsing, search, `~/.ssh/config`, terminals), without Flow Launcher;
- `Storage/`: the data file, its migration and the import of the 1.x data;
- `QueryHandler.cs`, `Main.cs`, `Settings/`: the Flow Launcher integration.

### Releasing

1. Update `Version` in `Flow.Launcher.Plugin.easyssh/plugin.json` and add the changes to `CHANGELOG.md`.
2. Merge into `master`. The *Release* workflow tests, packages and publishes the GitHub release `<version>`.
3. The [Flow Launcher plugin manifest](https://github.com/Flow-Launcher/Flow.Launcher.PluginsManifest) picks the
   release up within a few hours and offers the update to users.

## Contributors

Thanks to [@shaffe-fr](https://github.com/shaffe-fr) for the search, custom shells and the many fixes.

<img src="Flow.Launcher.Plugin.easyssh/Images/app.png" alt="EasySSH" width="96" align="right">

EasySSH for Flow Launcher
=========================

[![Latest release](https://img.shields.io/github/v/release/Melv1no/Flow.Launcher.Plugin.easyssh)](https://github.com/Melv1no/Flow.Launcher.Plugin.easyssh/releases/latest)
[![CI](https://github.com/Melv1no/Flow.Launcher.Plugin.easyssh/actions/workflows/ci.yml/badge.svg)](https://github.com/Melv1no/Flow.Launcher.Plugin.easyssh/actions/workflows/ci.yml)

A [Flow Launcher](https://www.flowlauncher.com/) plugin to search, save and open SSH connections, built on your
OpenSSH configuration (`~/.ssh/config`).

![EasySSH](https://github.com/Melv1no/Flow.Launcher.Plugin.easyssh/assets/66535418/2b864355-51c6-4a9a-b4ad-9460cf9328d3)

- Type `ssh` and a few letters to find a host of your `~/.ssh/config`, press <kbd>Enter</kbd> to connect.
- Hosts you add with EasySSH are written in standard ssh_config syntax: `ssh prod` works in any terminal,
  VS Code Remote-SSH or scp, not only in Flow Launcher.
- Search ignores case and accents, and tolerates typos. Servers you already connected to are suggested.
- Open connections in Command Prompt, Windows Terminal, PowerShell, or any terminal you configure.
  Graphical clients (PuTTY, KiTTY, WinSCP…) are started directly.
- Manage everything from the search bar or from the plugin settings.

## Requirements

- Flow Launcher **2.0** or later.
- The OpenSSH client (`ssh.exe`). It ships with Windows 10/11; if it's missing, add **OpenSSH Client** in
  *Settings → System → Optional features*.

## Installation

Type `pm install EasySSH` in Flow Launcher, or download `easyssh.zip` from the
[latest release](https://github.com/Melv1no/Flow.Launcher.Plugin.easyssh/releases/latest) and unzip it into
`%APPDATA%\FlowLauncher\Plugins\`.

## Quick start

```
ssh add prod admin@203.0.113.10 -p 2222     save a host
ssh prod                                    find it, press Enter to connect
```

The first command writes this block to `~/.ssh/easyssh.config`:

```
Host prod
    HostName 203.0.113.10
    User admin
    Port 2222
```

## Commands

| Command | What it does |
|---|---|
| `ssh <search>` | Search your hosts (and saved commands). The last result connects to what you typed, without saving it. |
| `ssh add <name> <destination or command>` | Save a host, or update the one with that name. A name with spaces becomes an alias with dashes: `ssh add "My server" …` saves `My-server`. |
| `ssh edit <search>` | Put a host saved with EasySSH back in the search bar to change it. Other hosts open in your editor. |
| `ssh remove <search>` | Remove a host saved with EasySSH, or a saved command (a confirmation is asked). Alias: `rm`. |
| `ssh hosts <search>` | Search only (no direct connection). Aliases: `profiles`, `p`. |
| `ssh d <destination or command>` | Connect without saving. Alias: `direct`. |
| `ssh terminal` | Choose the terminal used to open connections. Alias: `shell`. |
| `ssh terminal add <name> <program> [arguments]` | Add a custom terminal (see below). `ssh terminal add <program>` also works. |
| `ssh terminal remove` | Remove a custom terminal. |
| `ssh config` | Open `~/.ssh/config`, the files it includes and `~/.ssh/easyssh.config`. |
| `ssh hidden` | Show again a host you hid. |
| `ssh help` | List the commands. Alias: `docs`. |

Press <kbd>Tab</kbd> to complete a command or a host name, and <kbd>Shift</kbd>+<kbd>Enter</kbd> (or right click)
on a result to copy its command, open an SFTP session, edit, remove or hide it, open its configuration file, or open
it in another terminal. <kbd>Ctrl</kbd>+<kbd>C</kbd> copies the command of the selected result.

## How hosts are stored

EasySSH reads your OpenSSH configuration: `~/.ssh/config` and every file it includes. Every `Host` that names a
single host (no `*`, `?` or `!`) is listed, with its `User`, `HostName`, `Port` and `ProxyJump`. The files are read
again when they change.

The hosts you add with EasySSH go to **`~/.ssh/easyssh.config`**. The first time, EasySSH adds these lines at the
top of `~/.ssh/config` (the previous version is kept as `config.easyssh.bak`), and never changes anything else in it:

```
# Added by EasySSH (Flow Launcher): the hosts saved with EasySSH are in easyssh.config.
# Keep this line before the first Host or Match block.
Include easyssh.config
```

- EasySSH changes only the hosts of `easyssh.config`; the hosts of your own files are read-only for it
  (*Edit* opens the file). It never adds a host whose name already exists, so it can't silently override yours.
- You can edit `easyssh.config` by hand: EasySSH rewrites only the host you change with it, comments included.
- If you remove the `Include` line, EasySSH warns you that ssh can no longer see its hosts and offers to add it again.
- A host hidden with *Hide from EasySSH* stays in your configuration; only the results skip it (`ssh hidden`
  shows it again).

### What `ssh add` accepts

A destination (`admin@10.0.0.5`, `ssh://admin@10.0.0.5:2222`) or a full ssh command. Its options become
ssh_config keywords:

| Command line | ssh_config |
|---|---|
| `-p 2222`, `-l admin`, `admin@` | `Port 2222`, `User admin` |
| `-i C:\Users\me\.ssh\key` | `IdentityFile ~/.ssh/key` |
| `-J bastion` | `ProxyJump bastion` |
| `-L 8080:localhost:80`, `-R`, `-D 1080` | `LocalForward 8080 localhost:80`, `RemoteForward`, `DynamicForward 1080` |
| `-A`, `-X`, `-Y`, `-C`, `-4`, `-6`, `-t`, `-q`, `-v` | `ForwardAgent yes`, `ForwardX11 yes`… |
| `-o Keyword=value` | `Keyword value` |
| `ssh host tmux attach` | `RemoteCommand tmux attach` |

What ssh_config can't express is saved as an EasySSH **command** instead (shown in the results like the hosts):
another program (`wsl ssh …`, `plink …`, `mosh …`), ssh options without config keyword (`-N`, `-f`, `-W`…),
options added to another host (`ssh add b2 bastion -p 2222`: `HostName bastion` would not use the options of
`bastion`), or a destination that gets options from `Host` patterns (`ssh add web web.corp` with a `Host *.corp`
block: ssh matches patterns against the name typed, so `web` would not get them). The result tells which one it will
be before you press <kbd>Enter</kbd>. A host named after its own server (`ssh add github.com git@github.com -i key`)
applies to every connection to that server, git's and scp's included: the result says so.

Before saving, EasySSH asks your ssh client to read the new file (`ssh -G`): a line ssh would refuse, which would
break every ssh connection, is never written.

**Quotes:** on Windows, only double quotes group words. A path in single quotes such as `-i 'C:\keys\id'` is converted
to double quotes automatically.

## Search

The search is case-insensitive and accent-insensitive. Several terms can be typed; each must match the host name or
its details (user, host name, jump host).

Results are ranked by relevance: exact matches before typo-tolerant ones, matches on the name before matches on the
details, names starting with what you typed first, then the connections you use the most, then the alphabetical
order. The search tolerates 1 error per 5 characters (substitution, insertion, deletion, transposition).

Servers of `~/.ssh/known_hosts` that are not hosts of your configuration are suggested below them (this can be
turned off in the settings).

## Terminals

| Terminal | How the command is run |
|---|---|
| **Command Prompt** (default) | `cmd.exe`, shown in the terminal chosen in *Windows Settings → For developers → Terminal*. |
| **Windows Terminal** | A new tab in the last Windows Terminal window, titled with the host name. |
| **Windows PowerShell** / **PowerShell 7** | The command is run by PowerShell. |

With Command Prompt and PowerShell, the window stays open when ssh fails, so you can read the error.
Uncheck *Keep the window open when the connection fails* in the settings to close it immediately.
Windows Terminal keeps a failed tab open by itself. Graphical programs (PuTTY, KiTTY, WinSCP…) open their own window:
they are started directly, without terminal.

### Custom terminals

Add them in the settings or with `ssh terminal add <name> <program> [arguments]`. In the arguments, `{command}` is
replaced by the command to run and `{name}` by the host name. Without `{command}`, the command is added at the end.

| Example | Program | Arguments |
|---|---|---|
| Windows Terminal, "Git Bash" profile | `wt.exe` | `-w 0 new-tab -p "Git Bash" {command}` |
| Alacritty | `alacritty.exe` | `-e {command}` |
| WezTerm | `wezterm.exe` | `start -- {command}` |

## Settings

*Flow Launcher → Settings → Plugins → EasySSH*:

- the terminal and the custom terminals;
- the hosts: those of `easyssh.config` can be added, edited (alias, HostName, User, Port, IdentityFile, ProxyJump
  and any other option) and deleted; the others open in your editor; any host can be hidden;
- the saved commands;
- the SSH files, and the `known_hosts` suggestions;
- the data folder and the import of EasySSH 1.x data.

## Data

- Hosts: `~/.ssh/easyssh.config` (previous version kept as `easyssh.config.bak`).
- Saved commands, terminals and options: `easyssh.json`, in Flow Launcher's plugin settings folder
  (`%APPDATA%\FlowLauncher\Settings\Plugins\Flow.Launcher.Plugin.easyssh\`). Each save is atomic and keeps the
  previous version as `easyssh.json.bak`; a damaged file is renamed `easyssh.json.damaged-<date>` and the backup used.

### Upgrading from EasySSH 1.x

On its first start, EasySSH 3 imports `%USERPROFILE%\.ssh\profiles.json`, whichever version wrote it, and custom
shells become custom terminals. A profile becomes a host of `~/.ssh/easyssh.config` (with a suffix such as `prod-2`
when the name is taken) only when `ssh <name>` then does exactly what the profile did and nothing changes for your
other connections. Otherwise it stays a saved command, which works as before: when its destination is another host
or another profile, when `Host` patterns would apply differently, or when it is named after its own server with
options (`Host 10.0.0.5` with a port would change every connection to 10.0.0.5). A profile that only ran
`ssh <host>` for a host of your configuration is not duplicated. The old file is left untouched; the import can be
run again from the settings (it adds nothing twice).

## Troubleshooting

- **Nothing happens / the window closes immediately:** check the command with `ssh d <your command>`, and keep
  *Keep the window open when the connection fails* checked to read the error.
- **`ssh prod` works in EasySSH but not in a terminal:** check that `~/.ssh/config` still starts with
  `Include easyssh.config` (`ssh config` warns you if not).
- **"OpenSSH client not found":** add *OpenSSH Client* in Windows optional features, or use a command with another
  program.
- **The plugin doesn't load:** look at Flow Launcher's log (type `sys`, then *Open Log Location*) and open an
  [issue](https://github.com/Melv1no/Flow.Launcher.Plugin.easyssh/issues) with the lines mentioning EasySSH.

## Development

Requirements: the .NET 9 SDK (or later) on Windows.

```powershell
dotnet build                     # build the plugin and the tests
dotnet test                      # run the tests (they only use temporary folders, never your ~/.ssh)
./scripts/package.ps1            # create artifacts/easyssh.zip, as published
./scripts/deploy-local.ps1       # install that zip in your Flow Launcher (the installed copy is kept in a backup)
./scripts/deploy-local.ps1 -ResetData   # same, and start from scratch: the 1.x data is imported again
```

The icons of `Flow.Launcher.Plugin.easyssh/Images` are drawn by `scripts/icons/generate-icons.cs` (colors, shapes
and badges are at the top of the file). To change them, edit the script and run it with the .NET 10 SDK:

```powershell
dotnet run scripts/icons/generate-icons.cs                              # write the PNG files
dotnet run scripts/icons/generate-icons.cs -- --preview preview.png     # also write a sheet of every size
```

The plugin is compiled against `Flow.Launcher.Plugin` 5.0.0, the API of Flow Launcher 2.0 (`MinimumAppVersion`
in `plugin.json`), so it can't use a newer API by mistake. The code is split in:

- `Core/`: pure logic (command parsing, ssh_config reading and conversion, search, terminals), without Flow Launcher;
- `Storage/`: the files: `easyssh.config` and the `Include` line, `easyssh.json`, the import of the 1.x data;
- `QueryHandler.cs`, `Main.cs`, `Settings/`: the Flow Launcher integration.

### Releasing

1. Update `Version` in `Flow.Launcher.Plugin.easyssh/plugin.json` and add the changes to `CHANGELOG.md`.
2. Merge into `master`. The *Release* workflow tests, packages and publishes the GitHub release `<version>`.
3. The [Flow Launcher plugin manifest](https://github.com/Flow-Launcher/Flow.Launcher.PluginsManifest) picks the
   release up within a few hours and offers the update to users.

## Contributors

Thanks to [@shaffe-fr](https://github.com/shaffe-fr) for the search, custom shells and the many fixes.

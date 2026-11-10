# Changelog

## 3.0.0

A complete rework of the plugin around the OpenSSH configuration. It requires Flow Launcher 2.0 or later.

### Added

- `~/.ssh/config` is the source of the hosts: every host it declares (following `Include`) is listed, with its user,
  host name, port and jump host.
- Hosts added with `ssh add` (or the settings) are written to `~/.ssh/easyssh.config`, in ssh_config syntax, so
  `ssh <name>` also works outside Flow Launcher. `~/.ssh/config` only gets one `Include easyssh.config` line.
  Command line options are converted to their ssh_config keywords (`-p`, `-i`, `-J`, `-L`, `-o`…).
- Commands that ssh_config can't express (`wsl ssh …`, `plink …`, `ssh -N …`) are kept as saved commands.
- Servers of `~/.ssh/known_hosts` are suggested while typing.
- Hosts can be hidden from the results (`ssh hidden` shows them again); duplicated aliases are flagged.
- SFTP session from the context menu; `ssh config` opens the configuration files.
- Graphical clients (PuTTY, KiTTY, WinSCP…) are started directly, without an empty console behind them (#2).
- Typing anything that is not a command searches the hosts, and the last result connects to what was typed.
- Terminal presets: Command Prompt, Windows Terminal (new tab), Windows PowerShell, PowerShell 7. The terminal can
  be selected from the search bar (`ssh terminal`) or the settings (#2).
- Custom terminals with `{command}` and `{name}` placeholders.
- Settings panel: terminal, custom terminals, hosts (editor with HostName, User, Port, IdentityFile, ProxyJump and
  any other option), saved commands, SSH files, data.
- Context menu: copy the command, SFTP, edit, remove, hide, open the file, open in another terminal, save a direct
  connection or a known server as a host.
- <kbd>Tab</kbd> completes commands and host names (#4); <kbd>Ctrl</kbd>+<kbd>C</kbd> copies the command.
- `ssh edit` and confirmation before removing.
- The window stays open when ssh fails, to read the error (can be disabled).
- Matching characters of the names are highlighted.
- New icon, and icons that tell what a result does: add, current choice, remove, warning.

### Fixed

- `ssh d` and profiles only opened an empty terminal (#9): a destination without `ssh` (`root@host`) is run with ssh.
- The plugin failed to start when `%USERPROFILE%\.ssh` did not exist (#3). Startup errors no longer prevent the plugin
  from loading: they are shown in the results.
- Profiles of EasySSH 1.1 were ignored by 1.2, then overwritten at the first change: they are now imported.
- Single-quoted paths (`-i 'C:\key'`) are converted to double quotes (#4).
- Custom shells whose path contains spaces.
- Start-up could take several seconds (a `where.exe` process was started): programs are now looked up directly.
- No more notification at every key stroke when OpenSSH is missing.
- The `p` and `direct` aliases announced by the help now exist.

### Changed

- Profiles are replaced by ssh_config hosts. `~/.ssh/profiles.json` is imported once and left untouched: profiles
  become hosts of `~/.ssh/easyssh.config` (or saved commands), custom shells become custom terminals.
- The other data (saved commands, terminals, options) is in Flow Launcher's plugin settings folder (`easyssh.json`).
  Every file is saved atomically with a backup.
- Results are ranked by tier, then prefix, then usage, then name.
- Built for .NET 9 and the Flow Launcher 2 plugin API; the package no longer ships Flow Launcher's own libraries.
- Releases are built and published by GitHub Actions when the version changes.

### Included from the unreleased 1.2.0.1 and 1.2.0.2

- Weighted, accent-insensitive and typo-tolerant search (#8), custom shell arguments (#7),
  resolution of the shell path (#10) and the `cmd.exe /c` fix (#11), by @shaffe-fr.

## 1.2.0.0

- Multi-term filtering of profiles (#5).

## 1.1.1.2

- Profiles stored in `%USERPROFILE%\.ssh\profiles.json` so they survive updates (#1).

## 1.1.0.0

- Saved profiles.

## 1.0.0.0

- First release: direct connection with `ssh d`.

# Changelog

## 3.0.0

A complete rework of the plugin. It requires Flow Launcher 2.0 or later.

### Added

- Hosts of `~/.ssh/config` (with `Include`) are listed with the profiles.
- Typing anything that is not a command searches the connections, and the last result connects to what was typed.
- Terminal presets: Command Prompt, Windows Terminal (new tab), Windows PowerShell, PowerShell 7. The terminal can
  be selected from the search bar (`ssh terminal`) or the settings (#2).
- Custom terminals with `{command}` and `{name}` placeholders.
- Settings panel: terminal, custom terminals, profiles (add, edit, rename, delete), `~/.ssh/config` hosts, data.
- Context menu: copy the command, edit, remove, open in another terminal, save a host or a direct connection as a profile.
- <kbd>Tab</kbd> completes commands and profile names (#4); <kbd>Ctrl</kbd>+<kbd>C</kbd> copies the command.
- `ssh edit` and confirmation before removing a profile.
- The window stays open when ssh fails, to read the error (can be disabled).
- Profile names with spaces (`ssh add "My server" …`).
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

- Data moved to Flow Launcher's plugin settings folder (`easyssh.json`), saved atomically with a backup.
  `~/.ssh/profiles.json` is imported once and left untouched.
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

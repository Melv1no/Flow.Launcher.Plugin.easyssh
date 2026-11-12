# Contributing to EasySSH

[English](#english) · [Français](#français)

## English

Thank you for helping with EasySSH! Bug reports, ideas, translations and code are all welcome.

### Reporting a bug

Look at the [existing issues](https://github.com/Melv1no/Flow.Launcher.Plugin.easyssh/issues) first, then open a new
one with:

- the versions of EasySSH (*Flow Launcher → Settings → Plugins → EasySSH*), Flow Launcher, Windows and OpenSSH
  (`ssh -V`), and the terminal selected in EasySSH;
- what you typed, what you expected and what happened instead;
- for a problem with a host, its `Host` block (from `~/.ssh/config`, a file it includes or `~/.ssh/easyssh.config`);
- if an error is shown or the plugin doesn't load, the lines mentioning EasySSH in Flow Launcher's log (type `sys`,
  then *Open Log Location*).

> [!WARNING]
> Issues are public. Replace host names, IP addresses, user names and paths with examples (`203.0.113.10`, `admin`,
> `~/.ssh/key`), and never share a private key.

### Suggesting a feature

Open an issue that describes what you want to do before writing a large change, so that we can agree on the design
first. EasySSH follows a few principles:

- **Minimal:** `ssh <search>`, <kbd>Enter</kbd>. A new feature doesn't add steps to that.
- **Your OpenSSH configuration is the source of truth.** EasySSH reads `~/.ssh/config` and the files it includes. It
  writes only `~/.ssh/easyssh.config`, plus one `Include` line at the top of `~/.ssh/config`.
- **Nothing depends on EasySSH:** what it saves works without it (`ssh <name>`, VS Code Remote-SSH, scp). A host is
  saved only when `ssh <name>` does exactly what was typed; anything else is saved as an EasySSH command.
- **Your data is safe:** files are written atomically, with a backup, and a configuration that ssh would refuse is
  never written.

### Translating

The texts are in `Flow.Launcher.Plugin.easyssh/Languages/`, one file per language; `en.xaml` is the reference.
Flow Launcher uses the file of its own language, and English when there is none.

To add a language:

1. Copy `en.xaml` to `<code>.xaml`, with the code of the language in Flow Launcher: `de`, `es`, `it`, `pt-br`,
   `zh-cn`… (the names of the files of Flow Launcher's own `Languages` folder, in
   `%LOCALAPPDATA%\FlowLauncher\app-<version>\`).
2. Translate the texts, not the `x:Key` names. Keep the placeholders (`{0}`, `{1}`), and don't start a text with `{`:
   XAML would read it as a markup extension.
3. Add the code to `Languages` in `Flow.Launcher.Plugin.easyssh.Tests/TranslationTests.cs`, then run `dotnet test`:
   the tests check that the file loads and has the keys and placeholders of `en.xaml`.

A new text in the code needs a key (`plugin_easyssh_…`) in every language file.

### Working on the code

#### Requirements

- Windows 10 or 11, with the [.NET 9 SDK](https://dotnet.microsoft.com/download) or later.
- The OpenSSH client (`ssh.exe`): some tests ask it to check configurations (`ssh -G`).
- Flow Launcher 2.0 or later, to try the plugin.

Any editor works; the solution is `Flow.Launcher.Plugin.easyssh.sln`.

#### Build, test and try

```powershell
dotnet build                             # build the plugin and the tests
dotnet test                              # run the tests
./scripts/package.ps1                    # create artifacts/easyssh.zip, the zip published on GitHub
./scripts/deploy-local.ps1               # install that zip in your Flow Launcher and restart it
./scripts/deploy-local.ps1 -ResetData    # same, starting from scratch: the 1.x data is imported again
```

`deploy-local.ps1` closes Flow Launcher, moves the installed EasySSH (and, with `-ResetData`, its data) to
`%APPDATA%\FlowLauncher\EasySSH-backups\`, installs the new package and starts Flow Launcher again. Other options:
`-Configuration Debug`, and `-PluginsDirectory` for a portable Flow Launcher.

The installed plugin works on your real files: it reads your `~/.ssh/config` and writes `~/.ssh/easyssh.config` when
you save a host. Back up your `~/.ssh` folder before trying a change to that code.

To debug, install a Debug build (`./scripts/deploy-local.ps1 -Configuration Debug`) and attach your debugger to
`Flow.Launcher.exe`. Errors are written to Flow Launcher's log, with line numbers.

#### Project layout

| Path | Contents |
|---|---|
| `Flow.Launcher.Plugin.easyssh/Core/` | Pure logic, without Flow Launcher: command lines, ssh_config reading and conversion, `known_hosts`, search, terminals. |
| `Flow.Launcher.Plugin.easyssh/Storage/` | The files: `easyssh.config` and the `Include` line, `easyssh.json`, the import of the 1.x data, atomic writes. |
| `Flow.Launcher.Plugin.easyssh/Main.cs`, `QueryHandler.cs`, `PluginServices.cs` | The Flow Launcher plugin: entry point, results of a query, actions. |
| `Flow.Launcher.Plugin.easyssh/Settings/` | The settings panel. |
| `Flow.Launcher.Plugin.easyssh/Languages/`, `Images/` | Texts and icons. |
| `Flow.Launcher.Plugin.easyssh.Tests/` | The tests (xUnit, NSubstitute). |
| `scripts/` | Packaging, local installation, icon generator. |

#### Rules

- Tests never read or write your real `~/.ssh` nor Flow Launcher's data: use `TempDirectory`, or `PluginFixture`,
  which runs the plugin on temporary folders and records the programs it would start instead of starting them.
- Every change of behavior comes with tests.
- `Core/` doesn't use Flow Launcher: keep the logic there, where it can be tested on its own.
- Every text shown to users is in the language files, in English and French at least.
- No warnings: the build has none, keep it that way. Nullable reference types are enabled.
- The plugin is compiled against `Flow.Launcher.Plugin` 5.0.0, the API of Flow Launcher 2.0 (`MinimumAppVersion` in
  `plugin.json`). Don't upgrade it without raising `MinimumAppVersion`.
- Follow the style of the code around: file-scoped namespaces, XML documentation on types and members, comments that
  explain why.

#### Icons

The icons of `Flow.Launcher.Plugin.easyssh/Images` are drawn by `scripts/icons/generate-icons.cs` (colors, shapes and
badges are at the top of the file). Edit the script, then run it with the .NET 10 SDK:

```powershell
dotnet run scripts/icons/generate-icons.cs                            # write the PNG files
dotnet run scripts/icons/generate-icons.cs -- --preview preview.png   # also write a sheet of every size
```

### Pull requests

1. Fork the repository and create a branch from `master`.
2. Make your change, with its tests. `dotnet build` shows no warnings and `dotnet test` passes.
3. Update `Readme.md` when users see the change, and add a line to `CHANGELOG.md` under `## Unreleased` (create the
   section at the top if needed).
4. Don't change the version: it changes when a release is made.
5. Open the pull request: what changes and why, the issue it fixes (`Fixes #12`), and how you tried it (Flow Launcher
   version, terminal).

Commit messages are a short sentence in English, in the imperative: `Import quoted identity files`. The CI builds,
tests and packages every pull request; the package can be downloaded from the run (*Artifacts*).

### Releasing (maintainers)

1. Change the version in `Flow.Launcher.Plugin.easyssh/plugin.json` and in
   `Flow.Launcher.Plugin.easyssh/Flow.Launcher.Plugin.easyssh.csproj`, and rename `## Unreleased` in `CHANGELOG.md`.
2. Merge into `master`. The *Release* workflow runs when `plugin.json` changes on `master`: it tests, packages and
   publishes the GitHub release `<version>` with `easyssh.zip`.
3. The [Flow Launcher plugin manifest](https://github.com/Flow-Launcher/Flow.Launcher.PluginsManifest) picks the
   release up within a few hours and offers the update to users. The asset must keep the name `easyssh.zip`.

---

## Français

Merci de votre aide ! Les signalements de bugs, les idées, les traductions et le code sont les bienvenus.

### Signaler un bug

Consultez d'abord les [issues existantes](https://github.com/Melv1no/Flow.Launcher.Plugin.easyssh/issues), puis
ouvrez-en une avec :

- les versions d'EasySSH (*Flow Launcher → Paramètres → Plugins → EasySSH*), de Flow Launcher, de Windows et
  d'OpenSSH (`ssh -V`), et le terminal choisi dans EasySSH ;
- ce que vous avez tapé, ce que vous attendiez et ce qui s'est passé ;
- pour un problème avec un hôte, son bloc `Host` (dans `~/.ssh/config`, un fichier qu'il inclut ou
  `~/.ssh/easyssh.config`) ;
- si une erreur s'affiche ou si le plugin ne se charge pas, les lignes qui mentionnent EasySSH dans le journal de Flow
  Launcher (tapez `sys`, puis *Ouvrir l'emplacement des logs*).

> [!WARNING]
> Les issues sont publiques. Remplacez les noms d'hôtes, adresses IP, noms d'utilisateur et chemins par des exemples
> (`203.0.113.10`, `admin`, `~/.ssh/key`), et ne partagez jamais de clé privée.

### Proposer une fonctionnalité

Ouvrez une issue qui décrit ce que vous voulez faire avant d'écrire une modification importante, pour qu'on se mette
d'accord sur la conception d'abord. EasySSH suit quelques principes :

- **Le minimum :** `ssh <recherche>`, <kbd>Entrée</kbd>. Une nouvelle fonctionnalité n'ajoute pas d'étape à ça.
- **Votre configuration OpenSSH fait foi.** EasySSH lit `~/.ssh/config` et les fichiers qu'il inclut. Il n'écrit que
  `~/.ssh/easyssh.config`, plus une ligne `Include` en haut de `~/.ssh/config`.
- **Rien ne dépend d'EasySSH :** ce qu'il enregistre marche sans lui (`ssh <nom>`, VS Code Remote-SSH, scp). Un hôte
  n'est enregistré que si `ssh <nom>` fait exactement ce qui a été tapé ; sinon, c'est une commande EasySSH.
- **Vos données sont protégées :** les fichiers sont écrits de façon atomique, avec une sauvegarde, et une
  configuration que ssh refuserait n'est jamais écrite.

### Traduire

Les textes sont dans `Flow.Launcher.Plugin.easyssh/Languages/`, un fichier par langue ; `en.xaml` sert de référence.
Flow Launcher utilise le fichier de sa propre langue, et l'anglais s'il n'y en a pas.

Pour ajouter une langue :

1. Copiez `en.xaml` en `<code>.xaml`, avec le code de la langue dans Flow Launcher : `de`, `es`, `it`, `pt-br`,
   `zh-cn`… (les noms des fichiers du dossier `Languages` de Flow Launcher, dans
   `%LOCALAPPDATA%\FlowLauncher\app-<version>\`).
2. Traduisez les textes, pas les clés `x:Key`. Gardez les marqueurs (`{0}`, `{1}`), et ne commencez pas un texte par
   `{` : XAML le lirait comme une extension de balisage.
3. Ajoutez le code à `Languages` dans `Flow.Launcher.Plugin.easyssh.Tests/TranslationTests.cs`, puis lancez
   `dotnet test` : les tests vérifient que le fichier se charge et qu'il a les clés et les marqueurs de `en.xaml`.

Un nouveau texte dans le code a besoin d'une clé (`plugin_easyssh_…`) dans chaque fichier de langue.

### Travailler sur le code

#### Prérequis

- Windows 10 ou 11, avec le [SDK .NET 9](https://dotnet.microsoft.com/download) ou plus récent.
- Le client OpenSSH (`ssh.exe`) : certains tests lui font vérifier des configurations (`ssh -G`).
- Flow Launcher 2.0 ou plus récent, pour essayer le plugin.

N'importe quel éditeur convient ; la solution est `Flow.Launcher.Plugin.easyssh.sln`.

#### Compiler, tester, essayer

```powershell
dotnet build                             # compile le plugin et les tests
dotnet test                              # lance les tests
./scripts/package.ps1                    # crée artifacts/easyssh.zip, le zip publié sur GitHub
./scripts/deploy-local.ps1               # installe ce zip dans votre Flow Launcher et le redémarre
./scripts/deploy-local.ps1 -ResetData    # pareil, en repartant de zéro : les données 1.x sont importées à nouveau
```

`deploy-local.ps1` ferme Flow Launcher, déplace l'EasySSH installé (et, avec `-ResetData`, ses données) dans
`%APPDATA%\FlowLauncher\EasySSH-backups\`, installe le nouveau paquet et relance Flow Launcher. Autres options :
`-Configuration Debug`, et `-PluginsDirectory` pour un Flow Launcher portable.

Le plugin installé travaille sur vos vrais fichiers : il lit votre `~/.ssh/config` et écrit `~/.ssh/easyssh.config`
quand vous enregistrez un hôte. Sauvegardez votre dossier `~/.ssh` avant d'essayer une modification de ce code.

Pour déboguer, installez une version Debug (`./scripts/deploy-local.ps1 -Configuration Debug`) et attachez votre
débogueur à `Flow.Launcher.exe`. Les erreurs sont écrites dans le journal de Flow Launcher, avec les numéros de ligne.

#### Organisation du projet

| Chemin | Contenu |
|---|---|
| `Flow.Launcher.Plugin.easyssh/Core/` | La logique pure, sans Flow Launcher : lignes de commande, lecture et conversion de ssh_config, `known_hosts`, recherche, terminaux. |
| `Flow.Launcher.Plugin.easyssh/Storage/` | Les fichiers : `easyssh.config` et la ligne `Include`, `easyssh.json`, l'import des données 1.x, les écritures atomiques. |
| `Flow.Launcher.Plugin.easyssh/Main.cs`, `QueryHandler.cs`, `PluginServices.cs` | Le plugin Flow Launcher : point d'entrée, résultats d'une recherche, actions. |
| `Flow.Launcher.Plugin.easyssh/Settings/` | Le panneau de paramètres. |
| `Flow.Launcher.Plugin.easyssh/Languages/`, `Images/` | Les textes et les icônes. |
| `Flow.Launcher.Plugin.easyssh.Tests/` | Les tests (xUnit, NSubstitute). |
| `scripts/` | Paquet, installation locale, générateur d'icônes. |

#### Règles

- Les tests ne lisent ni n'écrivent jamais votre vrai `~/.ssh` ni les données de Flow Launcher : utilisez
  `TempDirectory`, ou `PluginFixture`, qui fait tourner le plugin sur des dossiers temporaires et note les programmes
  qu'il lancerait au lieu de les lancer.
- Chaque changement de comportement est accompagné de tests.
- `Core/` n'utilise pas Flow Launcher : gardez-y la logique, pour pouvoir la tester seule.
- Tout texte affiché aux utilisateurs est dans les fichiers de langue, au moins en anglais et en français.
- Aucun avertissement : la compilation n'en a pas, gardez-la ainsi. Les types référence nullables sont activés.
- Le plugin est compilé avec `Flow.Launcher.Plugin` 5.0.0, l'API de Flow Launcher 2.0 (`MinimumAppVersion` dans
  `plugin.json`). Ne mettez pas ce paquet à jour sans relever `MinimumAppVersion`.
- Suivez le style du code existant : espaces de noms au niveau du fichier, documentation XML sur les types et les
  membres, commentaires qui expliquent pourquoi.

#### Icônes

Les icônes de `Flow.Launcher.Plugin.easyssh/Images` sont dessinées par `scripts/icons/generate-icons.cs` (couleurs,
formes et badges en haut du fichier). Modifiez le script, puis lancez-le avec le SDK .NET 10 :

```powershell
dotnet run scripts/icons/generate-icons.cs                            # écrit les fichiers PNG
dotnet run scripts/icons/generate-icons.cs -- --preview preview.png   # écrit aussi une planche de toutes les tailles
```

### Pull requests

1. Forkez le dépôt et créez une branche à partir de `master`.
2. Faites votre modification, avec ses tests. `dotnet build` n'affiche aucun avertissement et `dotnet test` passe.
3. Mettez à jour `Readme.md` quand les utilisateurs voient le changement, et ajoutez une ligne à `CHANGELOG.md` sous
   `## Unreleased` (créez la section en haut si besoin).
4. Ne changez pas la version : elle change au moment de la release.
5. Ouvrez la pull request : ce qui change et pourquoi, l'issue corrigée (`Fixes #12`) et comment vous l'avez essayée
   (version de Flow Launcher, terminal).

Les messages de commit sont une phrase courte en anglais, à l'impératif : `Import quoted identity files`. La CI
compile, teste et crée le paquet de chaque pull request ; il se télécharge depuis l'exécution (*Artifacts*).

### Publier une version (mainteneurs)

1. Changez la version dans `Flow.Launcher.Plugin.easyssh/plugin.json` et dans
   `Flow.Launcher.Plugin.easyssh/Flow.Launcher.Plugin.easyssh.csproj`, et renommez `## Unreleased` dans
   `CHANGELOG.md`.
2. Fusionnez dans `master`. Le workflow *Release* se lance quand `plugin.json` change sur `master` : il teste, crée le
   paquet et publie la release GitHub `<version>` avec `easyssh.zip`.
3. Le [manifeste des plugins de Flow Launcher](https://github.com/Flow-Launcher/Flow.Launcher.PluginsManifest) prend
   la release en compte en quelques heures et propose la mise à jour aux utilisateurs. Le fichier doit garder le nom
   `easyssh.zip`.

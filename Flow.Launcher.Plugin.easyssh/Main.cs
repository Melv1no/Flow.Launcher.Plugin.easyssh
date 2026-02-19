﻿using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.easyssh;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Flow.Launcher.Plugin.EasySsh
{
    /// <inheritdoc />
    public class EasySsh : IPlugin, IPluginI18n
    {
        public static PluginInitContext _pluginContext;
        private ProfileManager _profileManager;

        private const string CommandAdd = "add";
        private const string CommandRemove = "remove";
        private const string CommandProfiles = "profiles";
        private const string CommandDirectConnect = "d";
        private const string CommandCustomShell = "shell";
        private const string CommandDocs = "docs";

        private const string AppIconPath = "Images\\app.png";
        private const string AppRedIconPath = "Images\\app-red.png";
        private const string AppGreenIconPath = "Images\\app-green.png";

        private string _databasePath;
        private string _sshClient = "cmd.exe";

        private bool _isSshInstalled = true;
        private bool _isDatabaseCreated = true;

        /// <inheritdoc />
        public void Init(PluginInitContext context)
        {
            _pluginContext = context;

            _databasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".ssh\\profiles.json"
            );

            _profileManager = new ProfileManager(_databasePath);

            _isSshInstalled = Utils.IsSshInstalled();
            _isDatabaseCreated = File.Exists(_databasePath);
        }

        /// <inheritdoc />
        public string GetTranslatedPluginTitle() => GetTranslation("plugin_easyssh_plugin_name");

        /// <inheritdoc />
        public string GetTranslatedPluginDescription() => GetTranslation("plugin_easyssh_plugin_description");

        /// <inheritdoc />
        public List<Result> Query(Query query)
        {
            var results = new List<Result>();
            var raw = query?.Search ?? string.Empty;
            var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (!_isSshInstalled)
            {
                _pluginContext.API.ShowMsg(
                    GetTranslation("plugin_easyssh_sshnotinstalled_title"),
                    GetTranslation("plugin_easyssh_sshnotinstalled_subtitle"),
                    AppRedIconPath, true
                );
            }
            if (!_isDatabaseCreated)
            {
                _pluginContext.API.ShowMsg(
                    GetTranslation("plugin_easyssh_databasenotcreated_title"),
                    GetTranslation("plugin_easyssh_databasenotcreated_subtitle"),
                    AppRedIconPath, true
                );
            }

            if (parts.Length == 0)
            {
                results.Add(new Result
                {
                    Title = "EasySsh",
                    SubTitle = GetTranslation("plugin_easyssh_subtitle_default"),
                    IcoPath = AppIconPath
                });
                return results;
            }

            var verb = parts[0].ToLowerInvariant();

            switch (verb)
            {
                // ----------------- DOCS -----------------
                case CommandDocs:
                    {
                        results.Add(new Result
                        {
                            Title = "EasySsh Documentation",
                            SubTitle = "Open the project page on GitHub",
                            IcoPath = AppIconPath,
                            Action = _ =>
                            {
                                try
                                {
                                    Process.Start(new ProcessStartInfo
                                    {
                                        FileName = "https://github.com/Melv1no/Flow.Launcher.Plugin.easyssh",
                                        UseShellExecute = true
                                    });
                                }
                                catch (Exception ex)
                                {
                                    _pluginContext.API.ShowMsg("EasySsh", $"Failed to open docs: {ex.Message}", AppRedIconPath);
                                }
                                return true;
                            }
                        });
                        return results;
                    }

                // ----------------- ADD -----------------
                case CommandAdd:
                    {
                        if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[1]))
                        {
                            var name = parts[1];
                            var sshCommand = string.Join(' ', parts.Skip(2));

                            results.Add(new Result
                            {
                                Title = $"{GetTranslation("plugin_easyssh_title_commandadd")} {name}",
                                SubTitle = sshCommand,
                                IcoPath = AppGreenIconPath,
                                Action = _ =>
                                {
                                    _profileManager.UserData.Entries[name] = sshCommand;
                                    return true;
                                }
                            });
                            return results;
                        }

                        results.Add(new Result
                        {
                            Title = GetTranslation("plugin_easyssh_title_commandadd"),
                            SubTitle = GetTranslation("plugin_easyssh_subtitle_commandadd"),
                            IcoPath = AppIconPath,
                        });
                        return results;
                    }

                // ----------------- REMOVE -----------------
                case CommandRemove:
                    {
                        var remove = new List<Result>
                    {
                        new Result
                        {
                            Title = GetTranslation("plugin_easyssh_title_commandremove"),
                            SubTitle = GetTranslation("plugin_easyssh_subtitle_commandremove"),
                            IcoPath = AppIconPath
                        }
                    };

                        foreach (var kv in _profileManager.UserData.Entries.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                        {
                            var key = kv.Key; var val = kv.Value;
                            remove.Add(new Result
                            {
                                Title = key,
                                SubTitle = val,
                                IcoPath = AppRedIconPath,
                                Action = _ =>
                                {
                                    _profileManager.UserData.Entries.Remove(key);
                                    return true;
                                }
                            });
                        }
                        return remove;
                    }

                // ----------------- PROFILES -----------------
                case CommandProfiles:
                    {
                        var list = new List<Result>
                        {
                            new Result
                            {
                                Title = GetTranslation("plugin_easyssh_title_commandprofiles"),
                                SubTitle = GetTranslation("plugin_easyssh_subtitle_commandprofiles"),
                                IcoPath = AppIconPath,
                                Score = int.MaxValue
                            }
                        };

                        var searchTerms = parts.Skip(1)
                            .Select(t => t.Trim())
                            .Where(t => t.Length > 0)
                            .ToArray();

                        List<KeyValuePair<string, string>> filteredList;

                        if (searchTerms.Length == 0)
                        {
                            // Pas de recherche : laisser Flow Launcher trier par historique d'utilisation
                            filteredList = _profileManager.UserData.Entries.ToList();
                        }
                        else
                        {
                            // Recherche : tri alpha stable puis tri stable par pertinence
                            filteredList = _profileManager.UserData.Entries
                                .Where(kv => MatchesProfile(kv.Key, kv.Value, searchTerms))
                                .OrderBy(kv => RemoveDiacritics(kv.Key), StringComparer.OrdinalIgnoreCase)
                                .OrderBy(kv => ScoreProfile(kv.Key, kv.Value, searchTerms))
                                .ToList();
                        }

                        for (var i = 0; i < filteredList.Count; i++)
                        {
                            var key = filteredList[i].Key;
                            var val = filteredList[i].Value;
                            list.Add(new Result
                            {
                                Title = key,
                                SubTitle = val,
                                IcoPath = AppGreenIconPath,
                                Score = filteredList.Count - i,
                                // TODO: décommenter après upgrade SDK Flow.Launcher.Plugin > 4.0.0
                                // AddSelectedCount = searchTerms.Length == 0,
                                // RecordKey = key,
                                Action = _ =>
                                {
                                    RunSshCommand(val);
                                    return true;
                                }
                            });
                        }
                        return list;
                    }

                // ----------------- DIRECT -----------------
                case CommandDirectConnect:
                    {
                        var cmd = string.Join(' ', parts.Skip(1)).Trim();
                        if (string.IsNullOrWhiteSpace(cmd))
                        {
                            results.Add(new Result
                            {
                                Title = "EasySsh",
                                SubTitle = GetTranslation("plugin_easyssh_subtitle_commanddirectconnect"),
                                IcoPath = AppIconPath,
                            });
                            return results;
                        }

                        results.Add(new Result
                        {
                            Title = "EasySsh",
                            SubTitle = $"{GetTranslation("plugin_easyssh_subtitle_commanddirectconnect")} {cmd}",
                            IcoPath = AppIconPath,
                            Action = _ =>
                            {
                                RunSshCommand(cmd);
                                return true;
                            }
                        });
                        return results;
                    }

                // ----------------- SHELL -----------------
                case CommandCustomShell:
                    {
                        // shell add "<full-exe-path>"   (ou non-quoté si sans espaces)
                        // shell remove                  (liste et supprime)
                        if (parts.Length >= 2 && parts[1].Equals("add", StringComparison.OrdinalIgnoreCase))
                        {
                            var exe = ParseOneQuotedOrToken(raw, "shell", "add");
                            if (string.IsNullOrWhiteSpace(exe))
                            {
                                results.Add(new Result
                                {
                                    Title = GetTranslation("plugin_easyssh_title_commandshell_add"),
                                    SubTitle = GetTranslation("plugin_easyssh_subtitle_commandshell_add_usage_onlyexe"),
                                    IcoPath = AppIconPath
                                });
                                return results;
                            }

                            results.Add(new Result
                            {
                                Title = GetTranslation("plugin_easyssh_title_commandshell_add"),
                                SubTitle = exe,
                                IcoPath = AppGreenIconPath,
                                Action = _ =>
                                {
                                    // On n'utilise plus la valeur, juste la clé (exe). Valeur vide.
                                    _profileManager.UserData.CustomShell[exe] = string.Empty;

                                    // Auto-sélection si aucun shell sélectionné
                                    if (string.IsNullOrWhiteSpace(_profileManager.UserData.SelectedCustomShell))
                                    {
                                        _profileManager.UserData.SelectedCustomShell = exe;
                                        _profileManager.SaveConfiguration();
                                    }
                                    return true;
                                }
                            });
                            return results;
                        }

                        if (parts.Length >= 2 && parts[1].Equals("remove", StringComparison.OrdinalIgnoreCase))
                        {
                            var remove = new List<Result>
                        {
                            new Result
                            {
                                Title = GetTranslation("plugin_easyssh_title_commandshell_remove"),
                                SubTitle = GetTranslation("plugin_easyssh_subtitle_commandshell_remove"),
                                IcoPath = AppIconPath
                            }
                        };

                            foreach (var kv in _profileManager.UserData.CustomShell.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                            {
                                var exeKey = kv.Key;
                                remove.Add(new Result
                                {
                                    Title = exeKey,
                                    SubTitle = string.IsNullOrWhiteSpace(_profileManager.UserData.SelectedCustomShell) ? "" :
                                               string.Equals(_profileManager.UserData.SelectedCustomShell, exeKey, StringComparison.OrdinalIgnoreCase) ? "(selected)" : "",
                                    IcoPath = AppRedIconPath,
                                    Action = _ =>
                                    {
                                        _profileManager.UserData.CustomShell.Remove(exeKey);

                                        if (string.Equals(_profileManager.UserData.SelectedCustomShell, exeKey, StringComparison.OrdinalIgnoreCase))
                                        {
                                            _profileManager.UserData.SelectedCustomShell = null;
                                            _profileManager.SaveConfiguration();
                                        }
                                        return true;
                                    }
                                });
                            }
                            return remove;
                        }

                        // Aide + liste courante (marque le sélectionné)
                        results.Add(new Result
                        {
                            Title = "shell",
                            SubTitle = GetTranslation("plugin_easyssh_subtitle_commandshell_help_onlyexe"),
                            IcoPath = AppIconPath
                        });

                        foreach (var kv in _profileManager.UserData.CustomShell.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                        {
                            var isSelected = string.Equals(_profileManager.UserData.SelectedCustomShell, kv.Key, StringComparison.OrdinalIgnoreCase);
                            results.Add(new Result
                            {
                                Title = isSelected ? $"* {kv.Key}" : kv.Key,
                                SubTitle = isSelected ? "(selected)" : "",
                                IcoPath = isSelected ? AppGreenIconPath : AppIconPath
                            });
                        }
                        return results;
                    }

                // ----------------- DEFAULT -----------------
                default:
                    {
                        results.Add(new Result
                        {
                            Title = "EasySsh",
                            SubTitle = GetTranslation("plugin_easyssh_subtitle_default"),
                            IcoPath = AppIconPath
                        });
                        return results;
                    }
            }
        }

        public static string GetTranslation(string key) => _pluginContext.API.GetTranslation(key);

        /// <summary>
        /// Supprime les diacritiques (accents) d'une chaine pour que "e" corresponde a "e", etc.
        /// </summary>
        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (var c in normalized)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                    != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Calcule un score de pertinence pour un profil.
        /// Plus le score est bas, plus le profil est pertinent.
        /// 0 = match exact nom ET commande
        /// 1 = match exact nom seulement
        /// 2 = match exact commande seulement
        /// 3 = match fuzzy nom ET commande
        /// 4 = match fuzzy nom seulement
        /// 5 = match fuzzy commande seulement
        /// </summary>
        private static int ScoreProfile(string key, string value, string[] searchTerms)
        {
            var exactInKey = true;
            var exactInValue = true;
            var fuzzyInKey = true;
            var fuzzyInValue = true;

            foreach (var term in searchTerms)
            {
                if (!ContainsIgnoreAccents(key, term))
                    exactInKey = false;
                if (!ContainsIgnoreAccents(value, term))
                    exactInValue = false;
                if (!FuzzyContains(key, term))
                    fuzzyInKey = false;
                if (!FuzzyContains(value, term))
                    fuzzyInValue = false;
            }

            if (exactInKey && exactInValue) return 0;
            if (exactInKey)                 return 1;
            if (exactInValue)               return 2;
            if (fuzzyInKey && fuzzyInValue)  return 3;
            if (fuzzyInKey)                 return 4;
            return 5; // fuzzy commande seulement
        }

        /// <summary>
        /// Vérifie si un profil matche tous les termes (exact ou fuzzy).
        /// </summary>
        private static bool MatchesProfile(string key, string value, string[] searchTerms)
        {
            return searchTerms.All(term =>
                ContainsIgnoreAccents(key, term)
                || ContainsIgnoreAccents(value, term)
                || FuzzyContains(key, term)
                || FuzzyContains(value, term));
        }

        /// <summary>
        /// Contains insensible aux accents et a la casse.
        /// </summary>
        private static bool ContainsIgnoreAccents(string source, string term)
        {
            return RemoveDiacritics(source).Contains(RemoveDiacritics(term), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Recherche approximative : vérifie si <paramref name="source"/> contient une sous-chaîne
        /// proche de <paramref name="term"/> avec une tolérance de 1 erreur pour 5 caractères.
        /// Utilise la distance de Levenshtein sur une fenêtre glissante.
        /// </summary>
        private static bool FuzzyContains(string source, string term)
        {
            var s = RemoveDiacritics(source).ToLowerInvariant();
            var t = RemoveDiacritics(term).ToLowerInvariant();

            if (t.Length == 0) return true;
            var maxErrors = t.Length / 5;

            // Moins de 5 caractères → pas de tolérance, match exact uniquement
            if (maxErrors == 0) return s.Contains(t);

            // Fenêtre glissante : on teste chaque sous-chaîne de longueur [t.Length - maxErrors .. t.Length + maxErrors]
            for (var start = 0; start < s.Length; start++)
            {
                var endMax = Math.Min(s.Length, start + t.Length + maxErrors);
                var endMin = Math.Max(start + 1, start + t.Length - maxErrors);

                for (var end = endMin; end <= endMax; end++)
                {
                    if (DamerauLevenshteinDistance(s, start, end, t) <= maxErrors)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Distance de Damerau-Levenshtein entre s[start..end) et t.
        /// Compte insertions, suppressions, substitutions ET transpositions adjacentes comme 1 opération chacune.
        /// </summary>
        private static int DamerauLevenshteinDistance(string s, int start, int end, string t)
        {
            var sLen = end - start;
            var tLen = t.Length;
            var d = new int[sLen + 1, tLen + 1];

            for (var i = 0; i <= sLen; i++) d[i, 0] = i;
            for (var j = 0; j <= tLen; j++) d[0, j] = j;

            for (var i = 1; i <= sLen; i++)
            {
                for (var j = 1; j <= tLen; j++)
                {
                    var cost = s[start + i - 1] == t[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);

                    // Transposition
                    if (i > 1 && j > 1
                        && s[start + i - 1] == t[j - 2]
                        && s[start + i - 2] == t[j - 1])
                    {
                        d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + cost);
                    }
                }
            }
            return d[sLen, tLen];
        }

        private void RunSshCommand(string originalSshCmd)
        {
            // Si un shell custom est sélectionné → <exe> <originalSshCmd>
            var selected = _profileManager.UserData.SelectedCustomShell;
            if (!string.IsNullOrWhiteSpace(selected) &&
                _profileManager.UserData.CustomShell.ContainsKey(selected))
            {
                var psiShell = new ProcessStartInfo
                {
                    FileName = selected,
                    Arguments = originalSshCmd,
                    RedirectStandardInput = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };
                new Process { StartInfo = psiShell }.Start();
                return;
            }

            // Sinon fallback : cmd.exe <originalSshCmd>
            var psi = new ProcessStartInfo
            {
                FileName = _sshClient,
                Arguments = originalSshCmd,
                RedirectStandardInput = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                UseShellExecute = true,
                CreateNoWindow = false
            };
            new Process { StartInfo = psi }.Start();
        }

        /// <summary>
        /// Extrait 1 segment : soit une chaîne entre guillemets, soit un token jusqu’au prochain espace,
        /// à partir de la sous-chaîne qui suit "shell add".
        /// </summary>
        private static string ParseOneQuotedOrToken(string raw, string keyword1, string keyword2)
        {
            // Cherche la zone après "shell add"
            var idxShell = raw.IndexOf(keyword1, StringComparison.OrdinalIgnoreCase);
            if (idxShell < 0) return string.Empty;
            var afterShell = raw.Substring(idxShell + keyword1.Length).TrimStart();

            var idxCmd = afterShell.IndexOf(keyword2, StringComparison.OrdinalIgnoreCase);
            if (idxCmd < 0) return string.Empty;
            var s = afterShell.Substring(idxCmd + keyword2.Length).TrimStart();

            if (string.IsNullOrEmpty(s)) return string.Empty;

            // Si guillemets
            if (s[0] == '"')
            {
                int i = 1;
                var sb = new StringBuilder();
                while (i < s.Length)
                {
                    var c = s[i++];
                    if (c == '\\' && i < s.Length)
                    {
                        var n = s[i++];
                        sb.Append(n switch { '\"' => '\"', '\\' => '\\', _ => n });
                        continue;
                    }
                    if (c == '"') return sb.ToString();
                    sb.Append(c);
                }
                return string.Empty; // pas de fermeture
            }

            // Sinon token jusqu’à l’espace
            var space = s.IndexOf(' ');
            return space < 0 ? s : s.Substring(0, space);
        }
    }
}

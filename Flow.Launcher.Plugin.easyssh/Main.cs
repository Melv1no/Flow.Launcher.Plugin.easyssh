using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.easyssh;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

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
        private const string CommandCustomShell = "shell"; // réservé si tu l'actives plus tard

        private const string AppIconPath = "Images\\app.png";
        private const string AppRedIconPath = "Images\\app-red.png";
        private const string AppGreenIconPath = "Images\\app-green.png";

        private string _databasePath;
        private string _sshClient = "cmd.exe"; // garde ta valeur

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

            // états de santé
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
            // Prépare un résultat par défaut
            var results = new List<Result>();
            var raw = query?.Search ?? string.Empty;
            var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Messages d’état (non bloquants, mais visibles)
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

            // Si aucune commande saisie → hint générique
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

                // ----------------- PROFILES (list & run) -----------------
                case CommandProfiles:
                    {
                        var list = new List<Result>
                    {
                        new Result
                        {
                            Title = GetTranslation("plugin_easyssh_title_commandprofiles"),
                            SubTitle = GetTranslation("plugin_easyssh_subtitle_commandprofiles"),
                            IcoPath = AppIconPath
                        }
                    };

                        foreach (var kv in _profileManager.UserData.Entries.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                        {
                            var key = kv.Key; var val = kv.Value;
                            list.Add(new Result
                            {
                                Title = key,
                                SubTitle = val,
                                IcoPath = AppGreenIconPath,
                                Action = _ =>
                                {
                                    var psi = new ProcessStartInfo
                                    {
                                        FileName = _sshClient,
                                        Arguments = val,
                                        RedirectStandardInput = false,
                                        RedirectStandardOutput = false,
                                        RedirectStandardError = false,
                                        UseShellExecute = true,
                                        CreateNoWindow = false
                                    };
                                    new Process { StartInfo = psi }.Start();
                                    return true;
                                }
                            });
                        }
                        return list;
                    }

                // ----------------- DIRECT (d ...) -----------------
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
                                var psi = new ProcessStartInfo
                                {
                                    FileName = _sshClient,
                                    Arguments = cmd,
                                    RedirectStandardInput = false,
                                    RedirectStandardOutput = false,
                                    RedirectStandardError = false,
                                    UseShellExecute = true,
                                    CreateNoWindow = false
                                };
                                new Process { StartInfo = psi }.Start();
                                return true;
                            }
                        });
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
    }
}

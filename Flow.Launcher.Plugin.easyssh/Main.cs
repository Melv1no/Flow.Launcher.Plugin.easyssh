using Flow.Launcher.Plugin;
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

                // ----------------- SHELL -----------------
                case CommandCustomShell:
                    {
                        if (parts.Length >= 2 && parts[1].Equals("add", StringComparison.OrdinalIgnoreCase))
                        {
                            var after = raw.Substring(raw.IndexOf("shell", StringComparison.OrdinalIgnoreCase) + "shell".Length).TrimStart();
                            after = after.Substring(after.IndexOf("add", StringComparison.OrdinalIgnoreCase) + "add".Length).TrimStart();

                            var parsed = ParseTwoQuotedSegments(after);
                            if (parsed == null)
                            {
                                results.Add(new Result
                                {
                                    Title = GetTranslation("plugin_easyssh_title_commandshell_add"),
                                    SubTitle = GetTranslation("plugin_easyssh_subtitle_commandshell_add_usage"),
                                    IcoPath = AppIconPath
                                });
                                return results;
                            }

                            var (exe, template) = parsed.Value;
                            results.Add(new Result
                            {
                                Title = GetTranslation("plugin_easyssh_title_commandshell_add"),
                                SubTitle = $"{exe}  |  {template}",
                                IcoPath = AppGreenIconPath,
                                Action = _ =>
                                {
                                    _profileManager.UserData.CustomShell[exe] = template;
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
                                var exe = kv.Key; var templ = kv.Value;
                                remove.Add(new Result
                                {
                                    Title = exe,
                                    SubTitle = templ,
                                    IcoPath = AppRedIconPath,
                                    Action = _ =>
                                    {
                                        _profileManager.UserData.CustomShell.Remove(exe);
                                        return true;
                                    }
                                });
                            }
                            return remove;
                        }

                        results.Add(new Result
                        {
                            Title = "shell",
                            SubTitle = GetTranslation("plugin_easyssh_subtitle_commandshell_help"),
                            IcoPath = AppIconPath
                        });

                        foreach (var kv in _profileManager.UserData.CustomShell.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                        {
                            results.Add(new Result
                            {
                                Title = kv.Key,
                                SubTitle = kv.Value,
                                IcoPath = AppGreenIconPath
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

        private static (string exe, string template)? ParseTwoQuotedSegments(string s)
        {
            int i = 0;
            SkipWs();
            if (!TakeQuote(out var first)) return null;
            SkipWs();
            if (!TakeQuote(out var second)) return null;
            return (first, second);

            void SkipWs()
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            bool TakeQuote(out string value)
            {
                value = string.Empty;
                if (i >= s.Length || s[i] != '\"') return false;
                i++;
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
                    if (c == '\"') { value = sb.ToString(); return true; }
                    sb.Append(c);
                }
                return false;
            }
        }
    }
}

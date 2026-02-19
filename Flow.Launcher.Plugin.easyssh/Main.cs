using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.easyssh;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Flow.Launcher.Plugin.EasySsh
{
    /// <inheritdoc />
    public class EasySsh : IPlugin, IPluginI18n, ISettingProvider
    {
        public static PluginInitContext _pluginContext;
        private ProfileManager _profileManager;

        private const string CommandAdd = "add";
        private const string CommandRemove = "remove";
        private const string CommandProfiles = "profiles";
        private const string CommandDirectConnect = "d";
        private const string CommandDocs = "docs";

        private const string AppIconPath = "Images\\app.png";
        private const string AppRedIconPath = "Images\\app-red.png";
        private const string AppGreenIconPath = "Images\\app-green.png";

        private string _databasePath;
        private string _sshClient = "cmd.exe";
        private const string SshPlaceholder = "{ssh}";

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
            EnsureDefaultShellProfiles();

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

                        var searchTerms = parts.Skip(1)
                            .Select(t => t.Trim())
                            .Where(t => t.Length > 0)
                            .ToArray();

                        var entries = _profileManager.UserData.Entries;

                        var filtered = searchTerms.Length == 0
                            ? entries.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                            : entries
                                .Where(kv => searchTerms.All(term =>
                                    kv.Key.Contains(term, StringComparison.OrdinalIgnoreCase)
                                    || kv.Value.Contains(term, StringComparison.OrdinalIgnoreCase)))
                                .OrderBy(kv => searchTerms.All(term =>
                                    kv.Key.Contains(term, StringComparison.OrdinalIgnoreCase)) ? 0 : 1)
                                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase);

                        foreach (var kv in filtered)
                        {
                            var key = kv.Key; var val = kv.Value;
                            list.Add(new Result
                            {
                                Title = key,
                                SubTitle = val,
                                IcoPath = AppGreenIconPath,
                                Action = _ =>
                                {
                                    RunSshCommand(val);
                                    return true;
                                }
                            });
                        }
                        return list;
                    }

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

        public Control CreateSettingPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(10) };

            panel.Children.Add(new TextBlock
            {
                Text = "Terminal emulator executable",
                Margin = new Thickness(0, 0, 0, 4)
            });

            var exeBox = new TextBox
            {
                Text = _profileManager.UserData.SelectedCustomShell ?? "cmd.exe",
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(exeBox);

            panel.Children.Add(new TextBlock
            {
                Text = "Arguments template (must contain {ssh})",
                Margin = new Thickness(0, 0, 0, 4)
            });

            var selected = _profileManager.UserData.SelectedCustomShell ?? "cmd.exe";
            var currentTemplate = _profileManager.UserData.CustomShell.ContainsKey(selected)
                ? _profileManager.UserData.CustomShell[selected]
                : SshPlaceholder;

            var templateBox = new TextBox
            {
                Text = currentTemplate,
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(templateBox);

            panel.Children.Add(new TextBlock
            {
                Text = "Examples: cmd.exe + /k \"{ssh}\" | powershell.exe + -NoExit -Command \"{ssh}\" | kitty.exe + -e {ssh}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var saveButton = new Button
            {
                Content = "Save terminal settings",
                Width = 180,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            saveButton.Click += (_, _) =>
            {
                var exe = exeBox.Text?.Trim();
                var template = templateBox.Text?.Trim();

                if (string.IsNullOrWhiteSpace(exe))
                {
                    _pluginContext.API.ShowMsg("EasySSH", "Executable path cannot be empty.", AppRedIconPath);
                    return;
                }

                if (string.IsNullOrWhiteSpace(template))
                {
                    template = SshPlaceholder;
                }

                if (!template.Contains(SshPlaceholder, StringComparison.Ordinal))
                {
                    template = $"{template} {SshPlaceholder}".Trim();
                }

                _profileManager.UserData.CustomShell[exe] = template;
                _profileManager.UserData.SelectedCustomShell = exe;
                _profileManager.SaveConfiguration();

                _pluginContext.API.ShowMsg("EasySSH", $"Terminal updated: {exe}", AppGreenIconPath);
            };

            panel.Children.Add(saveButton);
            return new UserControl { Content = panel };
        }

        public void Save()
        {
            _profileManager.SaveConfiguration();
        }

        private void RunSshCommand(string originalSshCmd)
        {
            var selected = _profileManager.UserData.SelectedCustomShell;
            if (!string.IsNullOrWhiteSpace(selected) &&
                _profileManager.UserData.CustomShell.ContainsKey(selected))
            {
                var template = _profileManager.UserData.CustomShell[selected];
                var arguments = BuildShellArguments(template, originalSshCmd);

                var psiShell = new ProcessStartInfo
                {
                    FileName = selected,
                    Arguments = arguments,
                    RedirectStandardInput = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };
                new Process { StartInfo = psiShell }.Start();
                return;
            }

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

        private void EnsureDefaultShellProfiles()
        {
            var updated = false;

            if (!_profileManager.UserData.CustomShell.ContainsKey("cmd.exe"))
            {
                _profileManager.UserData.CustomShell["cmd.exe"] = "/k \"{ssh}\"";
                updated = true;
            }

            if (!_profileManager.UserData.CustomShell.ContainsKey("powershell.exe"))
            {
                _profileManager.UserData.CustomShell["powershell.exe"] = "-NoExit -Command \"{ssh}\"";
                updated = true;
            }

            if (string.IsNullOrWhiteSpace(_profileManager.UserData.SelectedCustomShell))
            {
                _profileManager.UserData.SelectedCustomShell = "cmd.exe";
                updated = true;
            }

            if (updated)
            {
                _profileManager.SaveConfiguration();
            }
        }

        private static string BuildShellArguments(string template, string sshCommand)
        {
            var effectiveTemplate = string.IsNullOrWhiteSpace(template) ? SshPlaceholder : template;
            if (!effectiveTemplate.Contains(SshPlaceholder, StringComparison.Ordinal))
            {
                effectiveTemplate = $"{effectiveTemplate} {SshPlaceholder}";
            }

            return effectiveTemplate.Replace(SshPlaceholder, sshCommand, StringComparison.Ordinal);
        }
    }
}

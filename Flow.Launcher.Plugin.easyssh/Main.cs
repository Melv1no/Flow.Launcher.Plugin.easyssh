using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Flow.Launcher.Plugin;
using System.Linq;
using Flow.Launcher.Plugin.easyssh;

namespace Flow.Launcher.Plugin.EasySsh
{
    /// <inheritdoc />
    public class EasySsh : IPlugin,IPluginI18n
    {
        public static PluginInitContext _pluginContext;
        private ProfileManager _profileManager;
        private ProcessStartInfo _sshProcessInfo;

        private const string CommandAdd = "add";
        private const string CommandRemove = "remove";
        private const string CommandProfiles = "profiles";
        private const string CommandDirectConnect = "d";
        private const string CommandCustomShell = "shell";

        private const string AppIconPath = "Images\\app.png";
        private const string AppRedIconPath = "Images\\app-red.png";
        private const string AppGreenIconPath = "Images\\app-green.png";

        private Dictionary<string, string> _sshClientsDetected;
        private string _database_path;
        private string _sshClient = "cmd.exe";

        private bool isSshInstalled = true;
        private bool isDatabaseCreated = true;
        
        /// <inheritdoc />
        public void Init(PluginInitContext context)
        {
            _pluginContext = context;
            _database_path  = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh\\profiles.json");
            _profileManager = new ProfileManager(_database_path);
            _sshProcessInfo = new ProcessStartInfo
            {
                FileName = $"{_sshClient}",
                RedirectStandardInput = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                UseShellExecute = true,
                CreateNoWindow = false
            };
            if (!Utils.IsSshInstalled())
            {
                isSshInstalled = false;
            }

            if (!_profileManager.IsDatabaseCreated())
            {
                try
                {
                    File.WriteAllText(_database_path, "{}");
                }
                catch (IOException e)
                {
                    isDatabaseCreated = false;
                }
            }

            _sshClientsDetected = _profileManager.GetCustomShells();

        }

        /// <inheritdoc />
        public string GetTranslatedPluginTitle()
        {
            return GetTranslation("plugin_easyssh_plugin_name");
        }

        /// <inheritdoc />
        public string GetTranslatedPluginDescription()
        {
            return GetTranslation("plugin_easyssh_plugin_description");
        }
        
        /// <inheritdoc />
        public List<Result> Query(Query query)
        {
            var easySshMainCmd = new Result();
            string subtitle;
            var command = query.Search.Split(' ');
            if (!isSshInstalled)
            {
                _pluginContext.API.ShowMsg(GetTranslation("plugin_easyssh_sshnotinstalled_title"),GetTranslation("plugin_easyssh_sshnotinstalled_subtitle"), AppRedIconPath,true);
            }   
            if (!isDatabaseCreated)
            {
                _pluginContext.API.ShowMsg(GetTranslation("plugin_easyssh_databasenotcreated_title"),GetTranslation("plugin_easyssh_databasenotcreated_subtitle"), AppRedIconPath,true);
            }
            switch (command[0].ToLower())
            {
                case CommandAdd:
                    if (command.Length >= 3 && !string.IsNullOrWhiteSpace(command[1]))
                    {
                        string sshCommand = string.Join(" ", command.Skip(2));
                        subtitle = sshCommand;
                        easySshMainCmd.Action = context =>
                        {
                            _profileManager.AddProfile(command[1], sshCommand);
                            return true;
                        };
                    }
                    else
                    {
                        subtitle = GetTranslation("plugin_easyssh_subtitle_commandadd");
                    }
                    break;
                case CommandRemove:
                    var removeResult = new List<Result>
                    {
                        new Result
                        {
                            Title = GetTranslation("plugin_easyssh_title_commandremove"),
                            IcoPath = AppIconPath
                        }
                    };

                    foreach (var profile in _profileManager.GetProfiles())
                    {
                        removeResult.Add(new Result
                        {
                            Title = profile.Name,
                            SubTitle = profile.Command,
                            IcoPath = AppRedIconPath,
                            Action = context =>
                            {
                                _profileManager.RemoveProfile(profile.Id);
                                return true;
                            }
                        });
                    }
                    return removeResult;
                case CommandProfiles:
                    var profilesResult = new List<Result>
                    {
                        new Result
                        {
                            Title = GetTranslation("plugin_easyssh_title_commandprofiles"),
                            IcoPath = AppIconPath
                        }
                    };

                    foreach (var profile in _profileManager.GetProfiles())
                    {
                        profilesResult.Add(new Result
                        {
                            Title = profile.Name,
                            SubTitle = profile.Command,
                            IcoPath = AppGreenIconPath,
                            Action = context =>
                            {
                                _sshProcessInfo.Arguments = profile.Command;
                                new Process { StartInfo = _sshProcessInfo }.Start();
                                return true;
                            }
                        });
                    }
                    return profilesResult;
                case CommandDirectConnect:
                    subtitle = GetTranslation("plugin_easyssh_subtitle_commanddirectconnect") + $" {query.Search[1..]}";
                    var commands = string.Join(" ", command.Skip(1));
                    easySshMainCmd.Action = context =>
                    {
                        _sshProcessInfo.Arguments = commands;
                        new Process { StartInfo = _sshProcessInfo }.Start();
                        return true;
                    };
                    break;
                case CommandCustomShell:
                    var shellResult = new List<Result>
                    {
                        new Result
                        {
                            Title = GetTranslation("plugin_easyssh_title_commandcustomshell"),
                            IcoPath = AppIconPath
                        }
                    };
                    
                    if (command.Length >= 2 && !string.IsNullOrWhiteSpace(command[1]))
                    {
                        string args = string.Join(" ", command.Skip(2));
                        
                        subtitle = args;
                        string[] splitted_arg = args.Split(' ');
                        easySshMainCmd.Action = context =>
                        {
                            string remainingArgs = string.Join(" ", splitted_arg[1..]);
    
                            _sshClientsDetected.Add(splitted_arg[0], remainingArgs);
                            _profileManager.AddCustomShell(splitted_arg[0], remainingArgs);
                            return true;
                        };
                    }
                    else
                    {
                        foreach (var client in _sshClientsDetected)
                        {
                            shellResult.Add(new Result
                            {
                                Title = client.Key,
                                SubTitle = client.Value,
                                IcoPath = AppGreenIconPath,
                                Action = context =>
                                {
                                    _sshClient = client.Value;
                                    _sshProcessInfo.FileName = _sshClient;
                                    return true;
                                }
                            });
                        }
                        shellResult.Insert(0,new Result
                        {
                            Title = "Custom Shell",
                            SubTitle = "type: add <name> <path>",
                            IcoPath = AppIconPath,
                            Action = context => { return true; }
                        });
                        return shellResult;
                    }
                    break;
                default:
                    subtitle = GetTranslation("plugin_easyssh_subtitle_default");
                    break;
            }

            easySshMainCmd.Title = "EasySsh";
            easySshMainCmd.SubTitle = subtitle;
            easySshMainCmd.IcoPath = AppIconPath;
            return new List<Result>() { easySshMainCmd };
        }

        /// <returns><c>string</c>return the translation for a key.</returns>
        public static string GetTranslation(string key)
        {
            return _pluginContext.API.GetTranslation(key);
        }   
    }
}

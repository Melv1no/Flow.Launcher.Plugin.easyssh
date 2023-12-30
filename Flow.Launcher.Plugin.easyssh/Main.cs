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
        private PluginInitContext _pluginContext;
        private ProfileManager _profileManager;
        private ProcessStartInfo _sshProcessInfo;

        private const string CommandAdd = "add";
        private const string CommandRemove = "remove";
        private const string CommandProfiles = "profiles";
        private const string CommandDirectConnect = "d";

        private const string AppIconPath = "Images\\app.png";
        private const string AppRedIconPath = "Images\\app-red.png";
        private const string AppGreenIconPath = "Images\\app-green.png";
        
        /// <inheritdoc />
        public void Init(PluginInitContext context)
        {
            if (!Utils.IsSshInstalled())
            {
                return;
            }

            _pluginContext = context;
            _profileManager = new ProfileManager(Path.Combine(_pluginContext.CurrentPluginMetadata.PluginDirectory, "profiles.json"));
            _sshProcessInfo = new ProcessStartInfo
            {
                FileName = "ssh.exe",
                RedirectStandardInput = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                UseShellExecute = true,
                CreateNoWindow = false
            };
        }

        /// <inheritdoc />
        public string GetTranslatedPluginTitle()
        {
            return _pluginContext.API.GetTranslation("plugin_easyssh_plugin_name");
        }

        /// <inheritdoc />
        public string GetTranslatedPluginDescription()
        {
            return _pluginContext.API.GetTranslation("plugin_easyssh_plugin_description");
        }
        
        /// <inheritdoc />
        public List<Result> Query(Query query)
        {
            var easySshMainCmd = new Result();
            string subtitle;
            var command = query.Search.Split(' ');

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
                        subtitle = _pluginContext.API.GetTranslation("plugin_easyssh_subtitle_commandadd");
                    }
                    break;
                case CommandRemove:
                    var removeResult = new List<Result>
                    {
                        new Result
                        {
                            Title = _pluginContext.API.GetTranslation("plugin_easyssh_title_commandremove"),
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
                            Title = _pluginContext.API.GetTranslation("plugin_easyssh_title_commandprofiles"),
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
                    subtitle = _pluginContext.API.GetTranslation("plugin_easyssh_subtitle_commanddirectconnect") + $" {query.Search[1..]}";
                    var commands = string.Join(" ", command.Skip(1));
                    easySshMainCmd.Action = context =>
                    {
                        _sshProcessInfo.Arguments = commands;
                        new Process { StartInfo = _sshProcessInfo }.Start();
                        return true;
                    };
                    break;
                default:
                    subtitle = _pluginContext.API.GetTranslation("plugin_easyssh_subtitle_default");
                    break;
            }

            easySshMainCmd.Title = "EasySsh";
            easySshMainCmd.SubTitle = $"{subtitle}";
            easySshMainCmd.IcoPath = AppIconPath;
            return new List<Result>() { easySshMainCmd };
        }
        
    }
}

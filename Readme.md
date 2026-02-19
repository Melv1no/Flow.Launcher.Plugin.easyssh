Flow.Launcher.Plugin.easyssh
==================

A plugin for the [Flow launcher](https://github.com/Flow-Launcher/Flow.Launcher).

Currently, this plugin enables you to establish an SSH connection using a single command line. In the future, I plan to integrate a NoSQL database to store various SSH connection profiles.
![image](https://github.com/Melv1no/Flow.Launcher.Plugin.easyssh/assets/66535418/2b864355-51c6-4a9a-b4ad-9460cf9328d3)

### Installation

    pm install EasySSH or download last release and unzip in %appdata%/FlowLauncher/plugins

### Usage

    ssh d <direct ssh args | root@127.0.0.1>
    ssh profiles (select profile to connect)

You can filter profiles by typing search terms after `profiles`:

    ssh profiles <search terms>

The search is case-insensitive and accent-insensitive. For example, typing `preprod` will match a profile named `Préprod-web`. Multiple terms are supported — all must match either the profile name or the SSH command.

    ssh remove (select profile to delete)
    ssh add <profile name | TestProfile> <ssh args | root@127.0.0.1>

### To do

    ssh scp d <file> <user@host>:<destination>
    ssh scp profiles <file> <destination>

dotnet build

taskkill /f /im Flow.Launcher.exe
taskkill /f /im Flow.Launcher.exe
xcopy "C:\Users\molivet\source\repos\Flow.Launcher.Plugin.easyssh\Flow.Launcher.Plugin.easyssh\bin\Debug\*" "C:\Users\molivet\AppData\Roaming\FlowLauncher\Plugins\EasySSH\" /E /Y
"C:\Users\molivet\AppData\Local\FlowLauncher\Flow.Launcher.exe"
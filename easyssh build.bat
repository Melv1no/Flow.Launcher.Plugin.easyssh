dotnet build

taskkill /f /im Flow.Launcher.exe
taskkill /f /im Flow.Launcher.exe
xcopy "C:\Users\Melvin OLIVET\RiderProjects\Flow.Launcher.Plugin.easyssh\Flow.Launcher.Plugin.easyssh\bin\Debug\*" "C:\Users\Melvin OLIVET\AppData\Roaming\FlowLauncher\Plugins\EasySSH\" /E /Y
"C:\Users\Melvin OLIVET\AppData\Local\FlowLauncher\Flow.Launcher.exe"
using System.Reflection;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace Flow.Launcher.Plugin.EasySsh.Tests;

public class AssetTests
{
    public static TheoryData<string> IconPaths()
    {
        var paths = new TheoryData<string>();
        foreach (var field in typeof(Icons).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
        {
            paths.Add((string)field.GetValue(null)!);
        }

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.PluginProject, "plugin.json")));
        paths.Add(manifest.RootElement.GetProperty("IcoPath").GetString()!);
        return paths;
    }

    [Theory]
    [MemberData(nameof(IconPaths))]
    public void Icons_exist_and_are_square_png_files(string relativePath)
    {
        var path = Path.Combine(Repository.PluginProject, relativePath);
        Assert.True(File.Exists(path), $"Missing icon {relativePath}");

        using var stream = File.OpenRead(path);
        var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        Assert.Equal(frame.PixelWidth, frame.PixelHeight);
        Assert.True(frame.PixelWidth >= 128, $"{relativePath} is too small for high-DPI screens");
    }
}

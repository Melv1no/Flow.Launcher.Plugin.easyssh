#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false

// Draws the EasySSH icons (vector, WPF) and writes them as 256x256 PNG files.
// Usage: dotnet run scripts/icons/generate-icons.cs [-- --preview <file.png>]
// Requires the .NET 10 SDK (file-based app) on Windows.

using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

var thread = new Thread(() => IconGenerator.Run(args));
thread.SetApartmentState(ApartmentState.STA); // WPF rendering needs an STA thread.
thread.Start();
thread.Join();

internal static class IconGenerator
{
    private const int Size = 256;

    // Rounded tile, with a corner radius concentric with the corner element (see CornerCenter).
    private static readonly Rect Tile = new(12, 12, 232, 232);
    private const double TileRadius = 54;
    private static readonly Point CornerCenter = new(Tile.Right - TileRadius, Tile.Top + TileRadius);

    private static readonly Color TileStart = Color.FromRgb(0x3B, 0x82, 0xF6);
    private static readonly Color TileEnd = Color.FromRgb(0x5B, 0x3C, 0xE4);
    private static readonly Color Cursor = Color.FromRgb(0x6E, 0xF0, 0xD0);
    private static readonly Color Shadow = Color.FromRgb(0x1A, 0x14, 0x5C);

    private enum Badge { None, Plus, Check, Minus, Exclamation }

    private static readonly (string File, Badge Badge, Color Top, Color Bottom)[] Icons =
    [
        ("app.png", Badge.None, default, default),
        ("add.png", Badge.Plus, Color.FromRgb(0x34, 0xD3, 0x99), Color.FromRgb(0x05, 0x96, 0x69)),
        ("selected.png", Badge.Check, Color.FromRgb(0x34, 0xD3, 0x99), Color.FromRgb(0x05, 0x96, 0x69)),
        ("remove.png", Badge.Minus, Color.FromRgb(0xF8, 0x71, 0x71), Color.FromRgb(0xDC, 0x26, 0x26)),
        ("warning.png", Badge.Exclamation, Color.FromRgb(0xFB, 0xBF, 0x24), Color.FromRgb(0xD9, 0x77, 0x06)),
    ];

    public static void Run(string[] args)
    {
        var images = Path.GetFullPath(Path.Combine(ScriptDirectory(), "..", "..", "Flow.Launcher.Plugin.easyssh", "Images"));
        foreach (var (file, badge, top, bottom) in Icons)
        {
            Save(Render(badge, top, bottom, Size), Path.Combine(images, file));
            Console.WriteLine($"Wrote {Path.Combine(images, file)}");
        }

        var previewIndex = Array.IndexOf(args, "--preview");
        if (previewIndex >= 0 && previewIndex + 1 < args.Length)
        {
            Save(RenderPreview(), args[previewIndex + 1]);
            Console.WriteLine($"Wrote preview {args[previewIndex + 1]}");
        }
    }

    private static Visual Icon(Badge badge, Color badgeTop, Color badgeBottom)
    {
        var root = new ContainerVisual();

        // Tile: diagonal gradient, soft light on the upper half, thin light rim for dark backgrounds.
        var tile = new DrawingVisual();
        using (var dc = tile.RenderOpen())
        {
            var shape = new RectangleGeometry(Tile, TileRadius, TileRadius);
            dc.DrawGeometry(TileBrush(), null, shape);
            var light = new LinearGradientBrush(
                [new GradientStop(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), 0), new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.6)],
                new Point(0.5, 0), new Point(0.5, 1));
            dc.DrawGeometry(light, null, shape);
            var rim = new RectangleGeometry(Rect.Inflate(Tile, -1.5, -1.5), TileRadius - 1.5, TileRadius - 1.5);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 3), rim);
        }

        root.Children.Add(tile);

        // Prompt ">_": white chevron, mint cursor.
        var prompt = new DrawingVisual { Effect = GlyphShadow() };
        using (var dc = prompt.RenderOpen())
        {
            dc.DrawGeometry(null, RoundPen(Colors.White, 24), Geometry.Parse("M 62,98 L 110,137 L 62,176"));
            dc.DrawLine(RoundPen(Cursor, 24), new Point(130, 176), new Point(184, 176));
        }

        root.Children.Add(prompt);

        // Corner: a padlock on the main icon, a status badge on the others.
        var corner = new DrawingVisual { Effect = GlyphShadow() };
        using (var dc = corner.RenderOpen())
        {
            if (badge == Badge.None)
            {
                DrawPadlock(dc);
            }
            else
            {
                DrawBadge(dc, badge, badgeTop, badgeBottom);
            }
        }

        root.Children.Add(corner);
        return root;
    }

    private static void DrawPadlock(DrawingContext dc)
    {
        var c = CornerCenter;
        // Shackle, then body with a keyhole cut in the tile color.
        dc.DrawGeometry(null, RoundPen(Colors.White, 9),
            Geometry.Parse($"M {c.X - 13},{c.Y + 2} L {c.X - 13},{c.Y - 8} A 13,13 0 0 1 {c.X + 13},{c.Y - 8} L {c.X + 13},{c.Y + 2}"));
        var body = new Rect(c.X - 24, c.Y - 4, 48, 38);
        dc.DrawRoundedRectangle(Brushes.White, null, body, 9, 9);
        var keyhole = new GeometryGroup();
        keyhole.Children.Add(new EllipseGeometry(new Point(c.X, c.Y + 11), 6, 6));
        keyhole.Children.Add(new RectangleGeometry(new Rect(c.X - 2.75, c.Y + 11, 5.5, 13), 2.75, 2.75));
        dc.DrawGeometry(TileBrush(), null, keyhole);
    }

    private static void DrawBadge(DrawingContext dc, Badge badge, Color top, Color bottom)
    {
        var c = CornerCenter;
        const double radius = 36;
        var fill = new LinearGradientBrush(top, bottom, new Point(0.5, 0), new Point(0.5, 1));
        dc.DrawEllipse(fill, new Pen(Brushes.White, 6), c, radius, radius);

        var pen = RoundPen(Colors.White, 11);
        switch (badge)
        {
            case Badge.Plus:
                dc.DrawLine(pen, new Point(c.X, c.Y - 17), new Point(c.X, c.Y + 17));
                dc.DrawLine(pen, new Point(c.X - 17, c.Y), new Point(c.X + 17, c.Y));
                break;
            case Badge.Check:
                dc.DrawGeometry(null, pen, Geometry.Parse($"M {c.X - 16},{c.Y + 1} L {c.X - 5},{c.Y + 12} L {c.X + 16},{c.Y - 10}"));
                break;
            case Badge.Minus:
                dc.DrawLine(pen, new Point(c.X - 17, c.Y), new Point(c.X + 17, c.Y));
                break;
            case Badge.Exclamation:
                dc.DrawLine(pen, new Point(c.X, c.Y - 19), new Point(c.X, c.Y + 3));
                dc.DrawEllipse(Brushes.White, null, new Point(c.X, c.Y + 18), 6.5, 6.5);
                break;
        }
    }

    /// <summary>The tile gradient in absolute coordinates, so a cut-out shows exactly the color behind it.</summary>
    private static Brush TileBrush() => new LinearGradientBrush(TileStart, TileEnd, Tile.TopLeft, Tile.BottomRight)
    {
        MappingMode = BrushMappingMode.Absolute,
    };

    private static Pen RoundPen(Color color, double thickness) => new(new SolidColorBrush(color), thickness)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
        LineJoin = PenLineJoin.Round,
    };

    private static DropShadowEffect GlyphShadow() => new()
    {
        Color = Shadow,
        Direction = 270,
        ShadowDepth = 3,
        BlurRadius = 10,
        Opacity = 0.35,
        RenderingBias = RenderingBias.Quality,
    };

    private static BitmapSource Render(Badge badge, Color top, Color bottom, int pixels)
    {
        var visual = Icon(badge, top, bottom);
        var host = new ContainerVisual { Transform = new ScaleTransform(pixels / (double)Size, pixels / (double)Size) };
        host.Children.Add(visual);
        var bitmap = new RenderTargetBitmap(pixels, pixels, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>All icons at several sizes, on a dark and a light background, to check them by eye.</summary>
    private static BitmapSource RenderPreview()
    {
        int[] sizes = [256, 64, 48, 32, 24];
        var columnWidth = sizes.Sum() + 16 * sizes.Length;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            for (var band = 0; band < 2; band++)
            {
                var background = band == 0 ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3);
                dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(band * columnWidth, 0, columnWidth, Icons.Length * 272));
                for (var row = 0; row < Icons.Length; row++)
                {
                    var x = band * columnWidth + 8.0;
                    foreach (var size in sizes)
                    {
                        var (_, badge, top, bottom) = Icons[row];
                        dc.DrawImage(Render(badge, top, bottom, size), new Rect(x, row * 272 + 8, size, size));
                        x += size + 16;
                    }
                }
            }
        }

        var bitmap = new RenderTargetBitmap(columnWidth * 2, Icons.Length * 272, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string ScriptDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}

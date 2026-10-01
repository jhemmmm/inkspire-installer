using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Inkspire.Panel.UI;

/// <summary>The embedded Inkspire icon, plus tray variants with a status dot.</summary>
public static class AppIcon
{
    private static Icon? _main;
    private static readonly Dictionary<Color, Icon> Dotted = new();

    public static Icon Main => _main ??= Load();

    private static Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("Inkspire.Panel.Runtime.Resources.inkspire.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream);
    }

    /// <summary>The app icon with a coloured dot in the bottom-right corner (cached per colour).</summary>
    public static Icon WithDot(Color dot)
    {
        if (Dotted.TryGetValue(dot, out var cached)) return cached;
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawIcon(Main, new Rectangle(0, 0, 32, 32));
            using var ring = new SolidBrush(Color.White);
            using var fill = new SolidBrush(dot);
            g.FillEllipse(ring, 15, 15, 17, 17);
            g.FillEllipse(fill, 17, 17, 13, 13);
        }
        var handle = bmp.GetHicon();
        try
        {
            var icon = (Icon)Icon.FromHandle(handle).Clone(); // clone so the GDI handle can be released
            Dotted[dot] = icon;
            return icon;
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}

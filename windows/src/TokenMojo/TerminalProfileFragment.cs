using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TokenMojo.Core;

namespace TokenMojo;

/// <summary>
/// Writes the Windows Terminal fragment for <see cref="TerminalProfile"/> and its icon, a Claude-orange tile with a white
/// spark, into Windows Terminal's fragments folder. Rewritten only when it differs; a failure is traced, never shown,
/// since a session still starts without it (in the default profile, with its icon).
/// </summary>
internal static class TerminalProfileFragment
{
    private const int IconSize = 64;
    private static readonly Color ClaudeOrange = Color.FromRgb(0xD9, 0x77, 0x57);

    /// <summary>Draws the icon here, since WPF renders on the UI thread, and leaves the files to a background task.</summary>
    public static void Ensure()
    {
        byte[] icon = DrawIcon();
        _ = Task.Run(() => Write(icon));
    }

    private static void Write(byte[] icon)
    {
        try
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows Terminal", "Fragments", "TokenMojo");
            Directory.CreateDirectory(folder);
            string iconPath = Path.Combine(folder, "claude.png");
            if (!File.Exists(iconPath) || !File.ReadAllBytes(iconPath).AsSpan().SequenceEqual(icon)) File.WriteAllBytes(iconPath, icon);
            string fragmentPath = Path.Combine(folder, "tokenmojo.json");
            string fragment = TerminalProfile.FragmentJson(iconPath);
            if (!File.Exists(fragmentPath) || File.ReadAllText(fragmentPath) != fragment) File.WriteAllText(fragmentPath, fragment);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("TokenMojo: cannot write the Windows Terminal profile: {0}", e.Message);
        }
    }

    private static byte[] DrawIcon()
    {
        DrawingVisual visual = new();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRoundedRectangle(new SolidColorBrush(ClaudeOrange), null, new Rect(0, 0, IconSize, IconSize), 14, 14);
            Point center = new(IconSize / 2.0, IconSize / 2.0);
            Pen ray = new(Brushes.White, 6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            for (int index = 0; index < 8; index++)
            {
                double angle = index * Math.PI / 4;
                double length = index % 2 == 0 ? 22 : 15;
                context.DrawLine(ray, center, new Point(center.X + length * Math.Cos(angle), center.Y + length * Math.Sin(angle)));
            }
        }
        RenderTargetBitmap bitmap = new(IconSize, IconSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using MemoryStream stream = new();
        encoder.Save(stream);
        return stream.ToArray();
    }
}

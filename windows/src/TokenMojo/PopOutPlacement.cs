using System.IO;
using System.Text.Json;

namespace TokenMojo;

/// <summary>Remembers where the pop-out was left and its size, as setFrameAutosaveName does on the Mac.</summary>
internal static class PopOutPlacement
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TokenMojo", "popout.json");

    /// <summary>
    /// The position is in physical pixels, so the check below matches the monitors' own coordinates at any scale
    /// factor. The size is in device-independent units, so it means the same on a monitor of any scale.
    /// A size of zero means none was saved (a file written before the pop-out could be resized).
    /// </summary>
    public sealed record Placement(System.Drawing.Point Position, double Width, double Height)
    {
        public bool HasSize => Width > 0 && Height > 0;
    }

    public static Placement? Load()
    {
        try
        {
            Saved? saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath));
            if (saved is null) return null;
            System.Drawing.Point position = new(saved.Left, saved.Top);
            // A monitor may have been unplugged or moved since.
            return System.Windows.Forms.Screen.AllScreens.Any(screen => screen.WorkingArea.Contains(position))
                ? new Placement(position, saved.Width, saved.Height)
                : null;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or JsonException)
        {
            return null;
        }
    }

    public static void Save(Placement placement)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(
            new Saved(placement.Position.X, placement.Position.Y, placement.Width, placement.Height)));
    }

    private sealed record Saved(int Left, int Top, double Width = 0, double Height = 0);
}

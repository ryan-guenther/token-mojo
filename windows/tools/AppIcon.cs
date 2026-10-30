// Draws TokenMojo.ico, the exe's icon: the tray ring (track and a three-quarter arc) on a dark rounded tile,
// with no image library, so it runs on any OS. Regenerate with:
//   dotnet run windows/tools/AppIcon.cs -- windows/src/TokenMojo/TokenMojo.ico
using System.IO.Compression;

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];
if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet run AppIcon.cs -- <output.ico>");
    return 2;
}
byte[][] images = sizes.Select(size => Png(size, Render(size))).ToArray();
using (FileStream file = File.Create(args[0]))
using (BinaryWriter writer = new(file))
{
    writer.Write((short)0);
    writer.Write((short)1);
    writer.Write((short)sizes.Length);
    int offset = 6 + 16 * sizes.Length;
    for (int index = 0; index < sizes.Length; index++)
    {
        // 256 is written as 0 in the one-byte width and height.
        writer.Write((byte)(sizes[index] % 256));
        writer.Write((byte)(sizes[index] % 256));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(images[index].Length);
        writer.Write(offset);
        offset += images[index].Length;
    }
    foreach (byte[] image in images) writer.Write(image);
}
return 0;

// Straight RGBA rows, each pixel averaged over a 4x4 grid of samples in a unit square.
static byte[] Render(int size)
{
    const int Samples = 4;
    byte[] pixels = new byte[size * size * 4];
    for (int y = 0; y < size; y++)
    {
        for (int x = 0; x < size; x++)
        {
            double r = 0, g = 0, b = 0, a = 0;
            for (int sy = 0; sy < Samples; sy++)
            {
                for (int sx = 0; sx < Samples; sx++)
                {
                    (double R, double G, double B, double A) sample = Shade((x + (sx + 0.5) / Samples) / size, (y + (sy + 0.5) / Samples) / size);
                    r += sample.R * sample.A;
                    g += sample.G * sample.A;
                    b += sample.B * sample.A;
                    a += sample.A;
                }
            }
            int at = (y * size + x) * 4;
            if (a > 0)
            {
                pixels[at] = (byte)Math.Round(r / a * 255);
                pixels[at + 1] = (byte)Math.Round(g / a * 255);
                pixels[at + 2] = (byte)Math.Round(b / a * 255);
            }
            pixels[at + 3] = (byte)Math.Round(a / (Samples * Samples) * 255);
        }
    }
    return pixels;
}

// The colour at a point: the theme's dark window (#202020), a white track at the tray's 0x59 alpha, and the Ok green
// (#34C759) arc clockwise from twelve o'clock with round caps.
static (double R, double G, double B, double A) Shade(double x, double y)
{
    const double Corner = 0.22, Radius = 0.31, Thickness = 0.12, Sweep = 0.75;
    double qx = Math.Max(Math.Abs(x - 0.5) - (0.5 - Corner), 0), qy = Math.Max(Math.Abs(y - 0.5) - (0.5 - Corner), 0);
    if (Math.Sqrt(qx * qx + qy * qy) > Corner) return (0, 0, 0, 0);
    (double R, double G, double B) tile = (0x20 / 255.0, 0x20 / 255.0, 0x20 / 255.0);
    double dx = x - 0.5, dy = y - 0.5;
    double distance = Math.Sqrt(dx * dx + dy * dy);
    bool onRing = Math.Abs(distance - Radius) <= Thickness / 2;
    double turn = (Math.Atan2(dx, -dy) / (2 * Math.PI) + 1) % 1;
    double endAngle = 2 * Math.PI * Sweep;
    bool onCap = Near(dx, dy, 0, -Radius, Thickness / 2) || Near(dx, dy, Radius * Math.Sin(endAngle), -Radius * Math.Cos(endAngle), Thickness / 2);
    if ((onRing && turn <= Sweep) || onCap) return (0x34 / 255.0, 0xC7 / 255.0, 0x59 / 255.0, 1);
    if (onRing)
    {
        const double Track = 0x59 / 255.0;
        return (tile.R + (1 - tile.R) * Track, tile.G + (1 - tile.G) * Track, tile.B + (1 - tile.B) * Track, 1);
    }
    return (tile.R, tile.G, tile.B, 1);
}

static bool Near(double x, double y, double cx, double cy, double radius) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius;

static byte[] Png(int size, byte[] rgba)
{
    using MemoryStream raw = new();
    for (int y = 0; y < size; y++)
    {
        raw.WriteByte(0);
        raw.Write(rgba, y * size * 4, size * 4);
    }
    using MemoryStream compressed = new();
    using (ZLibStream zlib = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true)) raw.WriteTo(zlib);

    using MemoryStream png = new();
    png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    byte[] header = new byte[13];
    BigEndian(header, 0, size);
    BigEndian(header, 4, size);
    header[8] = 8;
    header[9] = 6;
    Chunk(png, "IHDR", header);
    Chunk(png, "IDAT", compressed.ToArray());
    Chunk(png, "IEND", []);
    return png.ToArray();
}

static void Chunk(Stream png, string type, byte[] data)
{
    byte[] length = new byte[4];
    BigEndian(length, 0, data.Length);
    png.Write(length);
    byte[] typed = [.. type.Select(c => (byte)c), .. data];
    png.Write(typed);
    byte[] crc = new byte[4];
    BigEndian(crc, 0, (int)Crc32(typed));
    png.Write(crc);
}

static void BigEndian(byte[] buffer, int at, int value)
{
    buffer[at] = (byte)(value >> 24);
    buffer[at + 1] = (byte)(value >> 16);
    buffer[at + 2] = (byte)(value >> 8);
    buffer[at + 3] = (byte)value;
}

static uint Crc32(byte[] data)
{
    uint crc = 0xFFFFFFFF;
    foreach (byte value in data)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
    }
    return ~crc;
}

using System.IO.Compression;
using StbImageSharp;

namespace FdgRaylib.Paper;

/// <summary>
/// A plain 8-bit RGBA picture in memory (row-major, 4 bytes a pixel) - what the paper-model cutter works on, with
/// just the steps it needs: crop, flip, scale down, and PNG in and out. No drawing library: the Godot build shows the
/// saved PNGs; this half stays testable without a window.
/// </summary>
public sealed class RgbaImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public RgbaImage(int width, int height, byte[]? pixels = null)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "An image needs a size.");
        Width = width;
        Height = height;
        Pixels = pixels ?? new byte[width * height * 4];
        if (Pixels.Length != width * height * 4) throw new ArgumentException("Pixel buffer is the wrong size.", nameof(pixels));
    }

    /// <summary>A white, opaque page.</summary>
    public static RgbaImage Blank(int width, int height, byte r = 255, byte g = 255, byte b = 255)
    {
        var img = new RgbaImage(width, height);
        for (int i = 0; i < img.Pixels.Length; i += 4) { img.Pixels[i] = r; img.Pixels[i + 1] = g; img.Pixels[i + 2] = b; img.Pixels[i + 3] = 255; }
        return img;
    }

    public int Index(int x, int y) => (y * Width + x) * 4;

    public (byte R, byte G, byte B, byte A) Get(int x, int y)
    {
        int i = Index(x, y);
        return (Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
    }

    public void Set(int x, int y, byte r, byte g, byte b, byte a = 255)
    {
        int i = Index(x, y);
        Pixels[i] = r; Pixels[i + 1] = g; Pixels[i + 2] = b; Pixels[i + 3] = a;
    }

    /// <summary>Fills a rectangle (clipped to the picture).</summary>
    public void Fill(int x, int y, int w, int h, byte r, byte g, byte b, byte a = 255)
    {
        for (int yy = Math.Max(0, y); yy < Math.Min(Height, y + h); yy++)
            for (int xx = Math.Max(0, x); xx < Math.Min(Width, x + w); xx++) Set(xx, yy, r, g, b, a);
    }

    /// <summary>A copy of a rectangle (clipped to the picture; at least 1x1).</summary>
    public RgbaImage Crop(PixelRect r)
    {
        r = r.ClipTo(Width, Height);
        if (r.W == 0 || r.H == 0) return new RgbaImage(1, 1);
        var o = new RgbaImage(r.W, r.H);
        for (int y = 0; y < o.Height; y++)
            Buffer.BlockCopy(Pixels, Index(r.X, r.Y + y), o.Pixels, y * o.Width * 4, o.Width * 4);
        return o;
    }

    /// <summary>Upside down (top row last) - the fold-back copy of a paper mini is printed this way.</summary>
    public RgbaImage FlipVertical()
    {
        var o = new RgbaImage(Width, Height);
        for (int y = 0; y < Height; y++) Buffer.BlockCopy(Pixels, y * Width * 4, o.Pixels, (Height - 1 - y) * Width * 4, Width * 4);
        return o;
    }

    public RgbaImage FlipHorizontal()
    {
        var o = new RgbaImage(Width, Height);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                Buffer.BlockCopy(Pixels, Index(x, y), o.Pixels, o.Index(Width - 1 - x, y), 4);
        return o;
    }

    /// <summary>
    /// Scaled so neither side is over <paramref name="maxW"/> x <paramref name="maxH"/> (never up). Box filter with
    /// colours weighted by alpha, so a cut-out's edge doesn't pick up a white or black fringe.
    /// </summary>
    public RgbaImage ScaleDownToFit(int maxW, int maxH)
    {
        double k = Math.Min(1.0, Math.Min(maxW / (double)Width, maxH / (double)Height));
        if (k >= 1.0) return this;
        int nw = Math.Max(1, (int)Math.Round(Width * k)), nh = Math.Max(1, (int)Math.Round(Height * k));
        var o = new RgbaImage(nw, nh);
        double sx = Width / (double)nw, sy = Height / (double)nh;
        for (int y = 0; y < nh; y++)
        {
            int y0 = (int)(y * sy), y1 = Math.Max(y0 + 1, (int)((y + 1) * sy));
            for (int x = 0; x < nw; x++)
            {
                int x0 = (int)(x * sx), x1 = Math.Max(x0 + 1, (int)((x + 1) * sx));
                double r = 0, g = 0, b = 0, a = 0;
                int n = 0;
                for (int yy = y0; yy < Math.Min(y1, Height); yy++)
                    for (int xx = x0; xx < Math.Min(x1, Width); xx++)
                    {
                        int i = Index(xx, yy);
                        double al = Pixels[i + 3];
                        r += Pixels[i] * al; g += Pixels[i + 1] * al; b += Pixels[i + 2] * al; a += al; n++;
                    }
                int j = o.Index(x, y);
                if (a > 0) { o.Pixels[j] = (byte)Math.Round(r / a); o.Pixels[j + 1] = (byte)Math.Round(g / a); o.Pixels[j + 2] = (byte)Math.Round(b / a); }
                o.Pixels[j + 3] = (byte)Math.Round(a / Math.Max(1, n));
            }
        }
        return o;
    }

    // ------------------------------------------------------------------ PNG

    public static RgbaImage LoadPng(string path) => FromBytes(File.ReadAllBytes(path));

    public static RgbaImage FromBytes(byte[] bytes)
    {
        var r = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
        return new RgbaImage(r.Width, r.Height, r.Data);
    }

    public void SavePng(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, ToPng());
    }

    /// <summary>
    /// The picture as a PNG: RGBA 8-bit, each row given the filter that packs it smallest (the usual "minimum sum of
    /// absolute differences" pick), deflated at the smallest setting - figures ship in the game's repo, so size counts.
    /// <paramref name="opaque"/> writes RGB only (page previews).
    /// </summary>
    public byte[] ToPng(bool opaque = false)
    {
        int bpp = opaque ? 3 : 4, stride = Width * bpp;
        var raw = new byte[Height * (stride + 1)];
        var prev = new byte[stride];
        var cur = new byte[stride];
        var trial = new byte[5][];
        for (int f = 0; f < 5; f++) trial[f] = new byte[stride];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int s = Index(x, y);
                for (int c = 0; c < bpp; c++) cur[x * bpp + c] = Pixels[s + c];
            }
            int best = 0;
            long bestSum = long.MaxValue;
            for (int f = 0; f < 5; f++)
            {
                long sum = 0;
                for (int i = 0; i < stride; i++)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                    int pred = f switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) >> 1, _ => Paeth(a, b, c) };
                    byte v = (byte)(cur[i] - pred);
                    trial[f][i] = v;
                    sum += v < 128 ? v : 256 - v;
                }
                if (sum < bestSum) { bestSum = sum; best = f; }
            }
            raw[y * (stride + 1)] = (byte)best;
            Buffer.BlockCopy(trial[best], 0, raw, y * (stride + 1) + 1, stride);
            (prev, cur) = (cur, prev);
        }

        using var ms = new MemoryStream();
        ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)Width);
        WriteBE(ihdr, 4, (uint)Height);
        ihdr[8] = 8;                     // bit depth
        ihdr[9] = (byte)(opaque ? 2 : 6); // RGB / RGBA
        Chunk(ms, "IHDR", ihdr);
        using (var z = new MemoryStream())
        {
            using (var zs = new ZLibStream(z, CompressionLevel.SmallestSize, true)) zs.Write(raw);
            Chunk(ms, "IDAT", z.ToArray());
        }
        Chunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteBE(byte[] buf, int at, uint v)
    {
        buf[at] = (byte)(v >> 24); buf[at + 1] = (byte)(v >> 16); buf[at + 2] = (byte)(v >> 8); buf[at + 3] = (byte)v;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, (uint)data.Length);
        s.Write(len);
        byte[] t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        uint crc = Crc(t, 0xFFFFFFFFu);
        crc = Crc(data, crc) ^ 0xFFFFFFFFu;
        var c = new byte[4];
        WriteBE(c, 0, crc);
        s.Write(c);
    }

    private static uint[]? _crcTable;

    private static uint Crc(byte[] data, uint crc)
    {
        _crcTable ??= Enumerable.Range(0, 256).Select(n =>
        {
            uint c = (uint)n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            return c;
        }).ToArray();
        foreach (byte b in data) crc = _crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}

/// <summary>A rectangle in whole pixels (x, y = top-left).</summary>
public readonly record struct PixelRect(int X, int Y, int W, int H)
{
    [Newtonsoft.Json.JsonIgnore] public int Right => X + W;
    [Newtonsoft.Json.JsonIgnore] public int Bottom => Y + H;
    [Newtonsoft.Json.JsonIgnore] public int Area => Math.Max(0, W) * Math.Max(0, H);

    public PixelRect ClipTo(int width, int height)
    {
        int x0 = Math.Clamp(X, 0, width), y0 = Math.Clamp(Y, 0, height);
        int x1 = Math.Clamp(Right, 0, width), y1 = Math.Clamp(Bottom, 0, height);
        return new PixelRect(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }

    public PixelRect Union(PixelRect o)
    {
        int x0 = Math.Min(X, o.X), y0 = Math.Min(Y, o.Y);
        return new PixelRect(x0, y0, Math.Max(Right, o.Right) - x0, Math.Max(Bottom, o.Bottom) - y0);
    }

    public PixelRect Intersect(PixelRect o)
    {
        int x0 = Math.Max(X, o.X), y0 = Math.Max(Y, o.Y);
        int x1 = Math.Min(Right, o.Right), y1 = Math.Min(Bottom, o.Bottom);
        return x1 <= x0 || y1 <= y0 ? new PixelRect(x0, y0, 0, 0) : new PixelRect(x0, y0, x1 - x0, y1 - y0);
    }

    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;

    public static PixelRect FromCorners(int x0, int y0, int x1, int y1) =>
        new(Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0), Math.Abs(y1 - y0));
}

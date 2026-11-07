using System.Numerics;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// Raylib-drawn text (banners, dice, the status strip, table markers) in the same DejaVu Sans the ImGui
/// panels use. These used to call <c>Raylib.DrawText</c>, which is raylib's built-in 8px pixel font scaled
/// up - blocky and a different typeface from everything around it (2026-11-06 look pass). Same signatures
/// as the calls they replace, so swapping is mechanical.
/// </summary>
internal static class RlText
{
    private const int AtlasSize = 72;   // rasterised once at this size, scaled down with bilinear filtering
    private static Font? _font;

    private static Font Font
    {
        get
        {
            if (_font is { } f) return f;
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "DejaVuSans.ttf");
            Font loaded = File.Exists(path) ? Raylib.LoadFontEx(path, AtlasSize, null, 0) : Raylib.GetFontDefault();
            Raylib.SetTextureFilter(loaded.Texture, TextureFilter.Bilinear);
            _font = loaded;
            return loaded;
        }
    }

    private static float Spacing(int fontSize) => MathF.Max(0f, fontSize / 20f);

    public static void Draw(string text, int x, int y, int fontSize, Color color) =>
        Raylib.DrawTextEx(Font, text, new Vector2(x, y), fontSize, Spacing(fontSize), color);

    public static int Measure(string text, int fontSize) =>
        (int)MathF.Ceiling(Raylib.MeasureTextEx(Font, text, fontSize, Spacing(fontSize)).X);
}

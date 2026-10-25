using System.Numerics;
using ImGuiNET;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// The UI art (Assets/UI, from the "Sci-Fi UI Minimalism" pack): button plates, panel title bars and tiles,
/// drawn behind ImGui widgets. Loaded the first time it's needed (after the window exists). If a file is
/// missing the widget simply draws the plain themed way.
/// </summary>
public static class UiSkin
{
    public enum EPlate { Go, Back, Commit }

    private static bool _tried;
    private static Texture2D? _go, _back, _commit, _title, _tileOn, _tileOff;

    private static void EnsureLoaded()
    {
        if (_tried || !Raylib.IsWindowReady()) return;
        _tried = true;
        _go = Load("ButtonGo.png");
        _back = Load("ButtonBack.png");
        _commit = Load("ButtonCommit.png");
        _title = Load("PanelTitle.png");
        _tileOn = Load("TileOn.png");
        _tileOff = Load("TileOff.png");
    }

    private static Texture2D? Load(string name)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "UI", name);
            if (!File.Exists(path)) return null;
            Texture2D t = Raylib.LoadTexture(path);
            if (t.Id == 0) return null;
            Raylib.SetTextureFilter(t, TextureFilter.Bilinear);
            return t;
        }
        catch
        {
            return null;
        }
    }

    public static bool HasButtons { get { EnsureLoaded(); return _go != null; } }

    /// <summary>Draws a button plate over the given screen rectangle (3-slice, so the arrows and corners
    /// keep their shape however wide the button is).</summary>
    public static void DrawPlate(ImDrawListPtr dl, EPlate plate, Vector2 min, Vector2 max, bool hovered, bool held)
    {
        EnsureLoaded();
        Texture2D? tex = plate switch { EPlate.Back => _back ?? _go, EPlate.Commit => _commit ?? _go, _ => _go };
        if (tex is not Texture2D t) return;
        float a = ImGui.GetStyle().Alpha;
        float lift = held ? 0.80f : hovered ? 1.35f : 1f;
        uint tint = ImGui.ColorConvertFloat4ToU32(new Vector4(MathF.Min(1f, lift), MathF.Min(1f, lift), MathF.Min(1f, lift), a));
        DrawThreeSlice(dl, t, min, max, capSource: 34f, tint);
        // A brighter edge on hover reads as "this is the one under the pointer" on the dark plates.
        if (hovered)
            dl.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.85f, 0.80f, 0.55f * a)), 2f);
    }

    /// <summary>The panel title bar image behind a heading line.</summary>
    public static void DrawTitleBar(ImDrawListPtr dl, Vector2 min, Vector2 max)
    {
        EnsureLoaded();
        if (_title is not Texture2D t) return;
        uint tint = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, ImGui.GetStyle().Alpha));
        DrawThreeSlice(dl, t, min, max, capSource: 40f, tint);
    }

    /// <summary>A selectable tile (tabs, chosen options): lit with the green underline when on.</summary>
    public static void DrawTile(ImDrawListPtr dl, bool on, Vector2 min, Vector2 max)
    {
        EnsureLoaded();
        if ((on ? _tileOn : _tileOff) is not Texture2D t) return;
        uint tint = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, ImGui.GetStyle().Alpha));
        DrawThreeSlice(dl, t, min, max, capSource: 8f, tint);
    }

    private static void DrawThreeSlice(ImDrawListPtr dl, Texture2D t, Vector2 min, Vector2 max, float capSource, uint tint)
    {
        float w = max.X - min.X, h = max.Y - min.Y;
        if (w <= 1f || h <= 1f) return;
        IntPtr id = (IntPtr)t.Id;
        float capU = MathF.Min(0.45f, capSource / t.Width);
        // The caps keep the source's proportions at this height, but never take more than 40% of the width.
        float capW = MathF.Min(w * 0.4f, capSource * h / t.Height);
        var l1 = new Vector2(min.X + capW, max.Y);
        var r0 = new Vector2(max.X - capW, min.Y);
        dl.AddImage(id, min, l1, new Vector2(0f, 0f), new Vector2(capU, 1f), tint);
        dl.AddImage(id, new Vector2(min.X + capW, min.Y), new Vector2(max.X - capW, max.Y),
            new Vector2(capU, 0f), new Vector2(1f - capU, 1f), tint);
        dl.AddImage(id, r0, max, new Vector2(1f - capU, 0f), new Vector2(1f, 1f), tint);
    }
}

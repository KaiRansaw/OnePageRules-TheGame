using FdgRaylib.Config;
using Godot;

namespace OprGodot.Hud;

/// <summary>
/// The hologram UI skin (Kai, 2026-11-16: "find a UI pack and remake the game's UI with it - a test, easy to roll
/// back"). Art: Wenrexa's Free / More UI Hologram Interface (CC0), in <c>res://ui/holo/</c> - teal for the Grimdark
/// look, recoloured gold for the Fantasy look. Every <see cref="ChamferBox"/> (the panel and button shape the whole
/// UI is built from) draws the pack's frames instead of its cut-corner polygons while this is on, and the theme uses the
/// pack's tick boxes and bars. Settings -> Display -> "Hologram UI (test)" turns it off again (UserConfig.View.UiSkin).
/// </summary>
public static class Skin
{
    public static bool Holo => UserConfig.Current.View.UiSkin != "classic";

    private static readonly Dictionary<string, Texture2D?> Cache = new();

    /// <summary>A pack texture in a look's colourway ("btn_normal", "frame"...).</summary>
    public static Texture2D? Tex(string name, Looks.ELook look)
    {
        string path = $"res://ui/holo/{(look == Looks.ELook.Fantasy ? "aof" : "gf")}/{name}.png";
        if (Cache.TryGetValue(path, out var t)) return t;
        t = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        return Cache[path] = t;
    }

    public static Texture2D? Tex(string name) => Tex(name, Looks.Current.Id);

    /// <summary>Turns the skin on or off and redraws everything (the root theme is rebuilt, which repaints every control).</summary>
    public static void Set(bool holo)
    {
        UserConfig.Current.View.UiSkin = holo ? "hologram" : "classic";
        UserConfig.Save();
        if (Engine.GetMainLoop() is SceneTree tree) tree.Root.Theme = HudTheme.Build();
    }

    /// <summary>A pack texture as a stretchable box (theme parts: tick-box backgrounds, bars).</summary>
    public static StyleBoxTexture Box(string name, int margin, int content = 0) => new()
    {
        Texture = Tex(name), TextureMarginLeft = margin, TextureMarginRight = margin, TextureMarginTop = margin, TextureMarginBottom = margin,
        ContentMarginLeft = content, ContentMarginRight = content, ContentMarginTop = content, ContentMarginBottom = content,
    };
}

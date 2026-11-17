using FdgRaylib.Config;
using Godot;

namespace OprGodot.Hud;

/// <summary>
/// UI themes made from art packs (Kai, 2026-11-16: "find a UI pack and remake the game's UI with it - easy to roll back;
/// we're going to go through a lot of different themes"). Each theme is a folder <c>res://ui/&lt;folder&gt;/</c> with a
/// <c>gf/</c> set (Grimdark look) and an <c>aof/</c> set (the same art recoloured for the Fantasy look), all with the same
/// file names: btn_normal / btn_hover / btn_active / btn_disabled (short boxes: buttons, chips, fields), fill_normal /
/// fill_hover / fill_active (selected or commit buttons), frame (panels), and optionally check_on / check_off /
/// check_disabled and bar_bg / bar_fg. A <see cref="Def"/> says how each piece stretches. Every <see cref="ChamferBox"/>
/// (the shape the whole UI is built from) draws the theme's art while a theme is picked; "classic" draws the cut-corner
/// panels. Settings -> Display -> UI theme picks one (UserConfig.View.UiSkin), straight away.
/// Adding a theme = a folder of art + one entry in <see cref="All"/>.
/// </summary>
public static class Skin
{
    /// <summary>A stretchable piece: how many pixels from each edge stay unstretched, and how far the art is grown
    /// past the box (art whose visible line sits inside a glow is grown so the line lands on the box edge).</summary>
    public readonly record struct Patch(float Left, float Top, float Right, float Bottom, float Grow = 0, bool Center = true)
    {
        public Patch(float all, float grow = 0, bool center = true) : this(all, all, all, all, grow, center) { }
    }

    public sealed record Def(string Id, string Label, string Folder, Patch Button, Patch Filled, Patch Frame,
        bool FrameOwnFill, int FillRadius, string Credit);

    public static readonly Def[] All =
    {
        new("hologram", "Hologram (Wenrexa)", "holo",
            Button: new Patch(12, grow: 1), Filled: new Patch(14, grow: 3), Frame: new Patch(38, grow: 18, center: false),
            FrameOwnFill: false, FillRadius: 14, Credit: "Wenrexa - Free / More UI Hologram Interface (CC0)"),
        new("minimal", "Sci-Fi Minimalism (Wenrexa)", "minimal",
            Button: new Patch(6), Filled: new Patch(5, 5, 5, 8), Frame: new Patch(26),
            FrameOwnFill: true, FillRadius: 4, Credit: "Wenrexa - UI Sci-Fi Minimalism #01 (CC0)"),
    };

    /// <summary>The picked theme; null = classic (the drawn cut-corner panels).</summary>
    public static Def? Current
    {
        get
        {
            string id = UserConfig.Current.View.UiSkin;
            return All.FirstOrDefault(d => d.Id == id);
        }
    }

    public static bool On => Current != null;

    private static readonly Dictionary<string, Texture2D?> Cache = new();

    /// <summary>A piece of the current theme in a look's colourway ("btn_normal", "frame"...); null when the theme has none.</summary>
    public static Texture2D? Tex(string name, Looks.ELook look)
    {
        if (Current is not { } def) return null;
        string path = $"res://ui/{def.Folder}/{(look == Looks.ELook.Fantasy ? "aof" : "gf")}/{name}.png";
        if (Cache.TryGetValue(path, out var t)) return t;
        t = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        return Cache[path] = t;
    }

    public static Texture2D? Tex(string name) => Tex(name, Looks.Current.Id);

    /// <summary>Picks a theme ("classic" or a <see cref="Def.Id"/>) and repaints everything (the root theme is rebuilt).</summary>
    public static void Set(string id)
    {
        UserConfig.Current.View.UiSkin = id;
        UserConfig.Save();
        if (Engine.GetMainLoop() is SceneTree tree) tree.Root.Theme = HudTheme.Build();
    }

    /// <summary>A theme texture as a stretchable box (theme parts: bars).</summary>
    public static StyleBoxTexture Box(string name, int margin, int content = 0) => new()
    {
        Texture = Tex(name), TextureMarginLeft = margin, TextureMarginRight = margin, TextureMarginTop = margin, TextureMarginBottom = margin,
        ContentMarginLeft = content, ContentMarginRight = content, ContentMarginTop = content, ContentMarginBottom = content,
    };
}

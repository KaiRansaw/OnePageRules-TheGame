using Godot;

namespace OprGodot;

/// <summary>
/// The game's colours, for code that draws or tints. The same values as the theme file
/// (scripts/hud/HudTheme.cs) and as the Raylib build's ImGuiTheme - the teal-slate with lime accent Kai
/// picked, which was always meant to carry over to Godot. Since 2026-11-16 the main colours follow the current look
/// (Hud.Looks: Grimdark or Fantasy); Lime is "the go colour" - lime in Grimdark, gold in Fantasy.
/// </summary>
public static class Palette
{
    private static Color R(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);

    public static Color Ink => Hud.Looks.Current.Ink;
    public static Color Panel => Hud.Looks.Current.Panel;
    public static Color PanelRaised => Hud.Looks.Current.PanelRaised;
    public static Color Frame => Hud.Looks.Current.Frame;
    public static Color Steel => Hud.Looks.Current.Steel;
    public static Color SteelDim => Hud.Looks.Current.SteelDim;
    public static Color Text => Hud.Looks.Current.Text;
    public static Color TextDim => Hud.Looks.Current.TextDim;
    public static Color Accent => Hud.Looks.Current.Accent;
    public static Color AccentHot => Hud.Looks.Current.AccentHot;
    public static Color AccentDim => Hud.Looks.Current.AccentDim;
    public static Color Lime => Hud.Looks.Current.Go;
    public static Color Header => Hud.Looks.Current.Header;
    public static readonly Color HitsYellow = new(0.96f, 0.92f, 0.42f);
    public static readonly Color WoundsRed = new(0.94f, 0.40f, 0.38f);
    public static readonly Color PointsBlue = new(0.58f, 0.76f, 1f);
    public static readonly Color HeroGold = new(1f, 0.85f, 0.3f);
    public static readonly Color RuleGold = new(0.92f, 0.82f, 0.45f);
}

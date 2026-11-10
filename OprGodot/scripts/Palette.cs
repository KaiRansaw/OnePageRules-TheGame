using Godot;

namespace OprGodot;

/// <summary>
/// The game's colours, for code that draws or tints. The same values as the theme file
/// (theme/opr_theme.tres) and as the Raylib build's ImGuiTheme - the teal-slate with lime accent Kai
/// picked, which was always meant to carry over to Godot. Change a colour in both places.
/// </summary>
public static class Palette
{
    private static Color R(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);

    public static readonly Color Ink = R(12, 18, 21);
    public static readonly Color Panel = R(18, 26, 30);
    public static readonly Color PanelRaised = R(26, 38, 43);
    public static readonly Color Frame = R(15, 23, 27);
    public static readonly Color Steel = R(72, 98, 106);
    public static readonly Color SteelDim = R(42, 60, 66);
    public static readonly Color Text = R(222, 231, 232);
    public static readonly Color TextDim = R(124, 142, 147);
    public static readonly Color Accent = R(31, 112, 100);
    public static readonly Color AccentHot = R(48, 150, 134);
    public static readonly Color AccentDim = R(19, 62, 58);
    public static readonly Color Lime = R(166, 214, 58);
    public static readonly Color Header = new(0.52f, 0.84f, 0.79f);
    public static readonly Color HitsYellow = new(0.96f, 0.92f, 0.42f);
    public static readonly Color WoundsRed = new(0.94f, 0.40f, 0.38f);
    public static readonly Color PointsBlue = new(0.58f, 0.76f, 1f);
    public static readonly Color HeroGold = new(1f, 0.85f, 0.3f);
    public static readonly Color RuleGold = new(0.92f, 0.82f, 0.45f);
}

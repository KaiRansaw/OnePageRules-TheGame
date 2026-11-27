using FDG;
using Godot;

namespace OprGodot.Hud;

/// <summary>
/// The two looks (Kai, 2026-11-16, "Menu Art &amp; Themes Mockup"): Grimdark Future keeps the dark glass, teal and lime
/// with Rajdhani / Exo 2; Age of Fantasy gets deep red panels, gold trim and Cinzel headings over Alegreya Sans text.
///
/// <para>
/// Every screen of a system uses its look. Screens read their colours and fonts from <see cref="HudKit"/> /
/// <see cref="Palette"/> when they build, and those read <see cref="Current"/> - so the look is set BEFORE a screen is
/// built (<see cref="App.Open{T}"/> does it for screens that say which system they belong to) and the root theme is
/// rebuilt with it. Screens with no system of their own (main menu, Asset Lab, Settings, Map Maker) use Grimdark.
/// </para>
/// </summary>
public static class Looks
{
    public enum ELook { Grimdark, Fantasy }

    public sealed class Look
    {
        public required ELook Id { get; init; }
        public required Color Ink, Panel, PanelRaised, Frame, Steel, SteelDim, Text, TextDim, Accent, AccentHot, AccentDim, Go, Header;
        public required Color Glass, GlassLight, Edge, EdgeHot;
        /// <summary>The fill of a picked / selected box (lime-tinted in Grimdark, gold-tinted in Fantasy).</summary>
        public required Color Picked;
        /// <summary>A deep panel sunk into another (journal records, mission marks, list backs).</summary>
        public required Color Well;
        /// <summary>Headings, buttons and numbers (Rajdhani Bold / Cinzel Bold).</summary>
        public required string BoldFont;
        public required string SemiFont, MediumFont;
        /// <summary>Big titles (Exo 2 variable / Cinzel ExtraBold).</summary>
        public required string TitleFont;
        /// <summary>Cinzel is wide: sizes are scaled by this so the same layouts still fit.</summary>
        public required float HeadingScale;
    }

    private static Color R(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);

    public static readonly Look Grimdark = new()
    {
        Id = ELook.Grimdark,
        Ink = R(12, 18, 21), Panel = R(18, 26, 30), PanelRaised = R(26, 38, 43), Frame = R(15, 23, 27),
        Steel = R(72, 98, 106), SteelDim = R(42, 60, 66), Text = R(222, 231, 232), TextDim = R(124, 142, 147),
        Accent = R(31, 112, 100), AccentHot = R(48, 150, 134), AccentDim = R(19, 62, 58), Go = R(166, 214, 58),
        Header = new(0.52f, 0.84f, 0.79f),
        Glass = new(0.045f, 0.07f, 0.08f, 0.86f), GlassLight = new(0.08f, 0.12f, 0.135f, 0.92f),
        Edge = new(0.25f, 0.36f, 0.39f, 0.95f), EdgeHot = new(0.40f, 0.78f, 0.70f, 1f),
        Picked = new(0.12f, 0.17f, 0.10f, 0.95f), Well = new(0.03f, 0.05f, 0.06f, 0.9f),
        BoldFont = "res://fonts/Rajdhani-Bold.ttf", SemiFont = "res://fonts/Rajdhani-SemiBold.ttf",
        MediumFont = "res://fonts/Rajdhani-Medium.ttf", TitleFont = "res://fonts/Exo2.ttf", HeadingScale = 1f,
    };

    /// <summary>The mockup's palette: panels #1e080a / #2b0d10, edge #6b4a2a, red #9c2b2b, gold #e3b55a, text #f1e6d2.</summary>
    public static readonly Look Fantasy = new()
    {
        Id = ELook.Fantasy,
        Ink = R(18, 6, 8), Panel = R(30, 8, 10), PanelRaised = R(43, 13, 16), Frame = R(24, 7, 9),
        Steel = R(140, 104, 64), SteelDim = R(76, 52, 34), Text = R(241, 230, 210), TextDim = R(185, 165, 138),
        Accent = R(120, 32, 32), AccentHot = R(176, 58, 52), AccentDim = R(70, 20, 22), Go = R(227, 181, 90),
        Header = R(232, 196, 120),
        Glass = new(0.118f, 0.031f, 0.039f, 0.88f), GlassLight = new(0.17f, 0.05f, 0.063f, 0.93f),
        Edge = new(0.42f, 0.29f, 0.165f, 0.95f), EdgeHot = new(0.89f, 0.71f, 0.35f, 1f),
        Picked = R(54, 36, 14, 0.95f), Well = R(16, 5, 7, 0.9f),
        BoldFont = "res://fonts/Cinzel-Bold.woff2", SemiFont = "res://fonts/AlegreyaSans-Bold.woff2",
        MediumFont = "res://fonts/AlegreyaSans-Medium.woff2", TitleFont = "res://fonts/Cinzel-ExtraBold.woff2", HeadingScale = 0.86f,
    };

    public static Look Current { get; private set; } = Grimdark;

    public static Look Get(ELook id) => id == ELook.Fantasy ? Fantasy : Grimdark;

    public static ELook For(EGameSystem system) => system is EGameSystem.AgeOfFantasy or EGameSystem.AoFSkirmish or EGameSystem.AoFQuest
        ? ELook.Fantasy : ELook.Grimdark;

    public static ELook For(GameScreen.EFamily family) => family == GameScreen.EFamily.AgeOfFantasy ? ELook.Fantasy : ELook.Grimdark;

    /// <summary>Switches the look for screens built from now on, and rebuilds the root theme (no-op when unchanged).</summary>
    public static void Use(ELook id, SceneTree? tree = null)
    {
        if (Current.Id == id) return;
        Current = Get(id);
        HudKit.ForgetFonts();
        var root = tree?.Root ?? (Engine.GetMainLoop() as SceneTree)?.Root;
        if (root != null) root.Theme = HudTheme.Build();
    }

    /// <summary>Builds something in another look without switching (the main menu's two halves).</summary>
    public static T With<T>(ELook id, Func<T> build)
    {
        Look was = Current;
        Current = Get(id);
        HudKit.ForgetFonts();
        try { return build(); }
        finally { Current = was; HudKit.ForgetFonts(); }
    }
}

/// <summary>A screen that belongs to one game system (or family) and is drawn in its look.</summary>
public interface ILooked
{
    Looks.ELook Look { get; }
}

using System.Numerics;
using FDG;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// One game system's mode picker: Army vs Army, Horde Mode, Campaign. Kai's mock, 2026-11-02 (screen
/// "1.2" in his numbering).
///
/// <para>
/// <b>One screen instance per system, opened with <see cref="Open"/>.</b> His mock originally had a
/// screen above this one that picked between Grimdark Future and Firefight, and he dropped it as
/// redundant once he saw both were already on the main menu - so a system tile now lands straight here.
/// </para>
///
/// <para>
/// The screen is shared by all four non-Quest systems and holds the picked one as state rather than
/// existing four times. That is the right kind of sharing and worth being explicit about, because the
/// wrong kind is the mistake this project keeps paying for: <b>what must never be shared is the DATA</b>
/// - which book, which points, which rules. Nothing here carries any; it passes
/// <see cref="EGameSystem"/> along and every downstream screen looks its own numbers up from that.
/// </para>
///
/// <para>
/// Deliberately plain under the working agreement: three buttons and a Back. The layout decision is
/// permanent and lives in <c>claude/campaign-flow-spec.md</c>; this drawing code does not survive the
/// Godot port.
/// </para>
/// </summary>
public sealed class ModeSelectScreen : IAppScreen
{
    /// <summary>Army vs Army - a standard game of this system.</summary>
    public Action<EGameSystem>? OnArmyVsArmy;

    /// <summary>Horde Mode for this system.</summary>
    public Action<EGameSystem>? OnHorde;

    /// <summary>This system's campaign.</summary>
    public Action<EGameSystem>? OnCampaign;

    public Action? OnBack;

    /// <summary>Which system's modes are being shown. Set by <see cref="Open"/>.</summary>
    public EGameSystem System { get; private set; } = EGameSystem.GrimdarkFuture;

    public void Open(EGameSystem system) => System = system;

    private static readonly Vector4 Dim = new(0.62f, 0.63f, 0.66f, 1f);

    /// <summary>What each mode is, in one line. Game text, so ASCII only (CLAUDE.md).</summary>
    private static string Blurb(string mode, EGameSystem system) => mode switch
    {
        "army" => system is EGameSystem.GFFirefight or EGameSystem.AoFSkirmish
            ? "Two warbands, one table. The usual game."
            : "Two armies, one table. The usual game.",
        "horde" => "Hold out against waves of enemies that get bigger every time.",
        _ => "A run of missions. Your force levels up, earns points and remembers losing.",
    };

    public void Draw(int screenW, int screenH)
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.11f, 0.11f, 0.11f, 1f));
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##modeselect",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse);

        bool useMenuFont = RaylibRenderer.MenuFontPx > 0f;
        if (useMenuFont) ImGui.PushFont(RaylibRenderer.MenuFont);

        // Sizes as fractions of the window height, the way the main menu sets its own (found 2026-11-05
        // playing it in a 1600x900 window: everything was drawn at the full menu font, so the blurbs ran
        // off the bottom and the Back button's label was cut in half).
        float basePx = useMenuFont ? RaylibRenderer.MenuFontPx : ImGui.GetFontSize();
        float titlePx = screenH * 0.045f, tilePx = screenH * 0.034f, blurbPx = screenH * 0.021f, backPx = screenH * 0.022f;

        float margin = screenW * 0.06f;
        string title = GameSystemInfo.DisplayName(System);
        ImGui.SetWindowFontScale(titlePx / basePx);
        Vector2 titleSize = ImGui.CalcTextSize(title);
        ImGui.SetCursorPos(new Vector2((screenW - titleSize.X) * 0.5f, screenH * 0.10f));
        ImGui.TextUnformatted(title);
        ImGui.SetWindowFontScale(1f);

        // Three tiles across the middle, the shape of his mock.
        float tileW = (screenW - margin * 2f - 40f) / 3f;
        float tileH = screenH * 0.28f;
        float top = screenH * 0.28f;

        Tile(0, "Army vs Army", Blurb("army", System), () => OnArmyVsArmy?.Invoke(System));
        Tile(1, "Horde Mode", Blurb("horde", System), () => OnHorde?.Invoke(System));
        Tile(2, "Campaign", Blurb("campaign", System), () => OnCampaign?.Invoke(System));

        ImGui.SetCursorPos(new Vector2(margin, screenH - screenH * 0.12f));
        ImGui.SetWindowFontScale(backPx / basePx);
        bool back = UiButton.Back("Back", new Vector2(Math.Max(180f, screenH * 0.2f), Math.Max(46f, screenH * 0.055f)));
        ImGui.SetWindowFontScale(1f);
        if (back || ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            OnBack?.Invoke();

        if (useMenuFont) ImGui.PopFont();
        ImGui.End();
        ImGui.PopStyleColor();

        void Tile(int index, string label, string blurb, Action go)
        {
            float x = margin + index * (tileW + 20f);
            ImGui.SetCursorPos(new Vector2(x, top));
            ImGui.SetWindowFontScale(tilePx / basePx);
            bool pressed = UiButton.Navigate(label + $"##mode{index}", new Vector2(tileW, tileH));
            ImGui.SetWindowFontScale(blurbPx / basePx);

            // The blurb under the tile rather than inside it: a button's label is its name, and a
            // sentence baked into the label makes the name harder to read at a glance.
            ImGui.SetCursorPos(new Vector2(x + 8f, top + tileH + 8f));
            ImGui.PushTextWrapPos(x + tileW);
            UiText.Colored(Dim, blurb, wrap: false);
            ImGui.PopTextWrapPos();
            ImGui.SetWindowFontScale(1f);
            if (pressed) go();
        }
    }
}

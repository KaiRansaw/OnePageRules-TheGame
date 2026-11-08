using System.Numerics;
using FDG;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// What a main-menu tile opens: one game family's three games side by side, each with its name, a blurb
/// and every mode it has as a button. Kai, 2026-11-07, "Main Menu Mockup" Option A. Replaces the old
/// per-system Mode Select and Quest Landing screens.
///
/// <para>Grimdark Future tile: Grimdark Future / GF: Firefight / GF: Star Quest. Age of Fantasy tile: Age
/// of Fantasy / AoF: Skirmish / AoF: Quest. The army games have Army vs Army, Horde Mode and Campaign; the
/// Quest games have Play a Mission, Horde Mode, Campaign and Build Heroes.</para>
///
/// <para>Each button only says WHICH system and WHICH mode; every downstream screen looks its own book,
/// points and rules up from that (CLAUDE.md: never share data between systems). Layout is rental.</para>
/// </summary>
public sealed class GameScreen : IAppScreen
{
    public enum EFamily { GrimdarkFuture, AgeOfFantasy }

    public enum EMode { ArmyVsArmy, Horde, Campaign, Mission, BuildHeroes }

    /// <summary>A mode button was pressed: which game, which mode.</summary>
    public Action<EGameSystem, EMode>? OnMode;

    public Action? OnBack;

    public EFamily Family { get; private set; } = EFamily.GrimdarkFuture;

    public void Open(EFamily family) => Family = family;

    /// <summary>The three games under a tile, in the order they are shown.</summary>
    public static EGameSystem[] Games(EFamily family) => family == EFamily.GrimdarkFuture
        ? new[] { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.GFStarQuest }
        : new[] { EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish, EGameSystem.AoFQuest };

    /// <summary>The mode buttons under one game, in order (Kai's wish list).</summary>
    public static EMode[] Modes(EGameSystem system) => GameSystemInfo.IsQuest(system)
        ? new[] { EMode.Mission, EMode.Horde, EMode.Campaign, EMode.BuildHeroes }
        : new[] { EMode.ArmyVsArmy, EMode.Horde, EMode.Campaign };

    public static string FamilyName(EFamily family) =>
        family == EFamily.GrimdarkFuture ? "Grimdark Future" : "Age of Fantasy";

    public static string ModeLabel(EMode mode) => mode switch
    {
        EMode.ArmyVsArmy => "Army vs Army",
        EMode.Horde => "Horde Mode",
        EMode.Campaign => "Campaign",
        EMode.Mission => "Play a Mission",
        _ => "Build Heroes",
    };

    /// <summary>One line on what a game is. Game text, ASCII only.</summary>
    public static string Blurb(EGameSystem system) => system switch
    {
        EGameSystem.GrimdarkFuture => "Full-scale sci-fi battles.",
        EGameSystem.AgeOfFantasy => "Full-scale fantasy battles.",
        EGameSystem.GFFirefight => "Small-squad sci-fi skirmish.",
        EGameSystem.AoFSkirmish => "Small-warband fantasy skirmish.",
        EGameSystem.GFStarQuest => "A party of heroes against the enemy's forces.",
        EGameSystem.AoFQuest => "A party of heroes against the enemy's forces.",
        _ => "",
    };

    /// <summary>The hover on a mode button: what that mode is, in plain words.</summary>
    public static string ModeTip(EGameSystem system, EMode mode) => mode switch
    {
        EMode.ArmyVsArmy => GameSystemInfo.IsSkirmish(system)
            ? "Two warbands, one table. The usual game. Goes straight to the lobby."
            : "Two armies, one table. The usual game. Goes straight to the lobby.",
        EMode.Horde => GameSystemInfo.IsQuest(system)
            ? "Hold out against waves of enemies until the party falls. Goes straight to the lobby."
            : "Hold out against waves of enemies that get bigger every time. Goes straight to the lobby.",
        EMode.Campaign => GameSystemInfo.IsQuest(system)
            ? "Chapters of missions with the same party, carried between them."
            : "A run of missions. Your force levels up, earns points and remembers losing.",
        EMode.Mission => "One mission from the Campaign Builder for your party. Goes straight to the lobby.",
        _ => "Make, level and equip the heroes your party is made of.",
    };

    private static readonly Vector4 Dim = new(0.62f, 0.63f, 0.66f, 1f);

    public void Draw(int screenW, int screenH)
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.11f, 0.11f, 0.11f, 1f));
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##gamescreen",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        bool useMenuFont = RaylibRenderer.MenuFontPx > 0f;
        if (useMenuFont) ImGui.PushFont(RaylibRenderer.MenuFont);
        float basePx = useMenuFont ? RaylibRenderer.MenuFontPx : ImGui.GetFontSize();
        float titlePx = screenH * 0.050f, namePx = screenH * 0.034f, blurbPx = screenH * 0.020f,
              btnPx = screenH * 0.024f;

        string title = FamilyName(Family);
        ImGui.SetWindowFontScale(titlePx / basePx);
        Vector2 titleSize = ImGui.CalcTextSize(title);
        ImGui.SetCursorPos(new Vector2((screenW - titleSize.X) * 0.5f, screenH * 0.07f));
        ImGui.TextUnformatted(title);
        ImGui.SetWindowFontScale(1f);

        float margin = screenW * 0.06f;
        float gap = screenW * 0.025f;
        float colW = (screenW - margin * 2f - gap * 2f) / 3f;
        float top = screenH * 0.20f;
        float colH = screenH * 0.64f;
        float btnH = MathF.Max(44f, screenH * 0.075f);

        EGameSystem[] games = Games(Family);
        for (int c = 0; c < games.Length; c++)
        {
            EGameSystem system = games[c];
            float x = margin + c * (colW + gap);
            ImGui.SetCursorPos(new Vector2(x, top));
            ImGui.BeginChild($"##game{c}", new Vector2(colW, colH), ImGuiChildFlags.Borders,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            float pad = colW * 0.06f;
            float inner = colW - pad * 2f;

            ImGui.SetCursorPos(new Vector2(pad, pad));
            ImGui.SetWindowFontScale(namePx / basePx);
            ImGui.TextUnformatted(GameSystemInfo.DisplayName(system));
            ImGui.SetWindowFontScale(blurbPx / basePx);
            ImGui.SetCursorPosX(pad);
            ImGui.PushTextWrapPos(pad + inner);
            UiText.Colored(Dim, Blurb(system));
            ImGui.PopTextWrapPos();

            EMode[] modes = Modes(system);
            // Buttons sit at the bottom of the column; the space above is kept for the game's art.
            float btnGap = btnH * 0.22f;
            float y = colH - pad - modes.Length * btnH - (modes.Length - 1) * btnGap;
            ImGui.SetWindowFontScale(btnPx / basePx);
            foreach (EMode mode in modes)
            {
                ImGui.SetCursorPos(new Vector2(pad, y));
                if (UiButton.Navigate($"{ModeLabel(mode)}##{system}{mode}", new Vector2(inner, btnH)))
                    OnMode?.Invoke(system, mode);
                if (ImGui.IsItemHovered()) UiText.TooltipWrapped(ModeTip(system, mode));
                y += btnH + btnGap;
            }
            ImGui.SetWindowFontScale(1f);
            ImGui.EndChild();
        }

        ImGui.SetCursorPos(new Vector2(margin, screenH - screenH * 0.10f));
        ImGui.SetWindowFontScale(btnPx / basePx);
        bool back = UiButton.Back("Back", new Vector2(Math.Max(180f, screenH * 0.2f), Math.Max(46f, screenH * 0.055f)));
        ImGui.SetWindowFontScale(1f);
        if (back || (ImGui.IsKeyPressed(ImGuiKey.Escape, false) && !ImGui.GetIO().WantTextInput
                     && !ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId)))
            OnBack?.Invoke();

        if (useMenuFont) ImGui.PopFont();
        ImGui.End();
        ImGui.PopStyleColor();
    }
}
